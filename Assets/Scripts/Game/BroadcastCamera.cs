using System.Collections.Generic;
using Pitch.Core;
using UnityEngine;

namespace Pitch.Game
{
    /// <summary>
    /// Broadcast camera: intro dolly, match follow cam with look-ahead, goal
    /// orbit, full-time orbit and camera shake. All smoothing is framerate
    /// independent, the render thread is never blocked.
    /// </summary>
    public sealed class BroadcastCamera : MonoBehaviour
    {
        private MatchEngine _match;
        private MatchView _view;
        private Camera _camera;
        private Vector3 _positionVelocity;
        private float _fovVelocity;
        private float _orbitAngle;
        private Vector3 _lookTarget;

        public void Initialise(MatchEngine match, MatchView view)
        {
            _match = match;
            _view = view;
            _camera = GetComponent<Camera>();
            transform.position = new Vector3(0f, 18f, -32f);
            transform.LookAt(Vector3.zero);
        }

        public void Rebind(MatchEngine match)
        {
            _match = match;
        }

        private void LateUpdate()
        {
            if (_match == null) return;
            var intro = _match.Intro;

            if (_match.Phase == MatchPhase.Intro && !intro.Finished)
            {
                var key = intro.CameraAt(intro.Elapsed);
                Vector3 position = key.Position.ToUnity();
                Vector3 look = key.LookAt.ToUnity();
                transform.position = Vector3.Lerp(transform.position, position, 1f - Mathf.Exp(-8f * Time.deltaTime));
                _lookTarget = Vector3.Lerp(_lookTarget, look, 1f - Mathf.Exp(-8f * Time.deltaTime));
                transform.LookAt(_lookTarget);
                _camera.fieldOfView = Mathf.SmoothDamp(_camera.fieldOfView, key.FieldOfView, ref _fovVelocity, 0.35f);
                return;
            }

            float alpha = _match.InterpolationAlpha;
            Vector3 ball = _match.Ball.Interpolated(alpha).ToUnity();
            float dir = _match.Home.AttackDir;
            Vector3 forward = new Vector3(0f, 0f, dir);

            Vector3 desired;
            Vector3 lookAt;
            float fov;

            if (_match.Phase == MatchPhase.GoalCelebration)
            {
                var scorer = _match.LastTouchPlayer;
                Vector3 focus = scorer != null ? scorer.Position.ToUnity() : ball;
                _orbitAngle += Time.deltaTime * 0.35f;
                desired = focus + Quaternion.Euler(0f, _orbitAngle, 0f) * new Vector3(0f, 4.5f, -8f);
                lookAt = focus + Vector3.up * 1.2f;
                fov = 34f;
            }
            else if (_match.Phase == MatchPhase.FullTime)
            {
                _orbitAngle += Time.deltaTime * 0.12f;
                desired = Quaternion.Euler(0f, _orbitAngle, 0f) * new Vector3(0f, 26f, -52f);
                lookAt = Vector3.zero;
                fov = 46f;
            }
            else
            {
                // Height rises with the ball, look-ahead follows the attacking direction.
                float ballHeight = Mathf.Clamp(ball.y, 0f, 8f);
                desired = ball - forward * (23f + ballHeight * 0.6f) + Vector3.up * (15.5f + ballHeight) + Vector3.right * 5f;
                lookAt = ball + forward * 7f + Vector3.up * 0.6f;
                fov = 40f - Mathf.Clamp01(Vec3.FlatDistance(_match.Ball.Position, PitchSpec.AttackingGoalFor(dir)) / 60f) * 5f;
            }

            desired = new Vector3(
                Mathf.Clamp(desired.x, -PitchSpec.HalfWidth - 16f, PitchSpec.HalfWidth + 16f),
                desired.y,
                Mathf.Clamp(desired.z, -PitchSpec.HalfLength - 24f, PitchSpec.HalfLength + 24f));

            float smooth = 1f - Mathf.Exp(-6f * Time.deltaTime);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _positionVelocity, 0.28f);
            _lookTarget = Vector3.Lerp(_lookTarget, lookAt, smooth);
            _camera.fieldOfView = Mathf.SmoothDamp(_camera.fieldOfView, fov, ref _fovVelocity, 0.5f);

            float shake = _match.Presentation.ShakeOffset(Time.time);
            transform.position += new Vector3(shake, -shake * 0.6f, 0f);
            transform.LookAt(_lookTarget);
        }
    }
}

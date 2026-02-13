// CMovement.cs
// BPSI - Brushstroke-Preserving Skeletal Interaction Method
// Movement + micro-"Alive" feedback + inactivity auto-reset (return along recorded path).
//
// Key behaviors (as described):
// - Click shrimp body: Alive micro-motion (no position change).
// - Click blank space: Move one or two shrimps toward target; auto TurnLeft/TurnRight based on direction.
// - No interaction for N seconds: return to initial pose along the recorded path (reverse traversal).
//
// Animator integration is OPTIONAL.
// If you attach an Animator, set up triggers with the names below (or change them in inspector):
//   Alive, Move, TurnLeft, TurnRight, Return, Idle
//
// NOTE: This script does NOT require a Rigidbody. If you use physics, you can adapt Move step accordingly.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BPSI
{
    public class CMovement : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Units per second.")]
        public float moveSpeed = 1.2f;

        [Tooltip("Degrees per second.")]
        public float rotateSpeed = 540f;

        [Tooltip("For 2D ink painting, rotation around Z is typical.")]
        public bool useZRotation = true;

        [Tooltip("Angle offset (deg) to align the shrimp sprite's forward direction with movement direction.")]
        public float spriteForwardAngle = 0f;

        [Tooltip("Distance threshold to consider the destination reached.")]
        public float arriveThreshold = 0.02f;

        [Header("Path Recording (for exact reverse return)")]
        [Tooltip("How often (seconds) to record points during motion. Smaller = more faithful return path.")]
        [Range(0.01f, 0.2f)]
        public float pathRecordInterval = 0.06f;

        [Tooltip("Max recorded points to avoid unbounded memory (motion is usually short).")]
        public int maxRecordedPoints = 400;

        [Header("Alive Micro-Motion (no translation)")]
        public float aliveWiggleDuration = 0.28f;
        public float aliveRotateAmplitudeDeg = 2.0f;

        [Tooltip("Optional subtle scale pulse (kept very small). Set 0 to disable.")]
        public float aliveScalePulse = 0.01f;

        [Header("Auto Reset / Return")]
        [Tooltip("If no user interaction happens for this many seconds, the shrimp returns.")]
        public float inactivityReturnSeconds = 3.0f;

        [Tooltip("Return speed multiplier relative to moveSpeed.")]
        public float returnSpeedMultiplier = 1.15f;

        [Header("Animator (Optional)")]
        public Animator animator;

        [Tooltip("Trigger parameter names (Animator). Leave empty to disable that trigger.")]
        public string trigAlive = "Alive";
        public string trigMove = "Move";
        public string trigTurnLeft = "TurnLeft";
        public string trigTurnRight = "TurnRight";
        public string trigReturn = "Return";
        public string trigIdle = "Idle";

        // Initial pose (the exact "original painting state")
        private Vector3 _initialPos;
        private Quaternion _initialRot;
        private Vector3 _initialScale;

        // Inactivity bookkeeping
        private float _lastUserInputTime;

        // Motion state
        private Coroutine _motionCo;
        private Coroutine _aliveCo;
        private Coroutine _watchdogCo;

        private readonly List<Vector3> _recordedPath = new List<Vector3>(256);

        private int _lastTurnSign = 0;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void Start()
        {
            _initialPos = transform.position;
            _initialRot = transform.rotation;
            _initialScale = transform.localScale;

            _lastUserInputTime = Time.time;

            _watchdogCo = StartCoroutine(InactivityWatchdog());
        }

        /// <summary>Called by input router: resets the inactivity timer.</summary>
        public void NotifyUserInteraction()
        {
            _lastUserInputTime = Time.time;
        }

        /// <summary>Alive micro-motion: small rotation/pulse, no translation.</summary>
        public void TriggerAlive()
        {
            StopMotion();
            if (_aliveCo != null) StopCoroutine(_aliveCo);
            _aliveCo = StartCoroutine(AliveWiggle());
        }

        /// <summary>Move toward the target (records path for later return).</summary>
        public void MoveTo(Vector3 targetWorldPos)
        {
            StopMotion();
            if (_aliveCo != null) StopCoroutine(_aliveCo);

            // Reset path and start recording from current pose
            _recordedPath.Clear();
            _recordedPath.Add(transform.position);

            FireTrigger(trigMove);

            _motionCo = StartCoroutine(MoveRoutine(targetWorldPos, recordPath: true, speedMul: 1f));
        }

        /// <summary>Return to the original pose (recorded path reverse if available; otherwise direct).</summary>
        public void ReturnToOrigin()
        {
            StopMotion();
            if (_aliveCo != null) StopCoroutine(_aliveCo);

            FireTrigger(trigReturn);

            if (_recordedPath.Count >= 2)
            {
                _motionCo = StartCoroutine(ReturnAlongRecordedPath());
            }
            else
            {
                // Direct fallback
                _motionCo = StartCoroutine(MoveRoutine(_initialPos, recordPath: false, speedMul: returnSpeedMultiplier, snapToInitialAtEnd: true));
            }
        }

        private void StopMotion()
        {
            if (_motionCo != null)
            {
                StopCoroutine(_motionCo);
                _motionCo = null;
            }
        }

        private IEnumerator AliveWiggle()
        {
            // Keep position unchanged; only rotate/pulse slightly then restore.
            Vector3 basePos = transform.position;
            Quaternion baseRot = transform.rotation;
            Vector3 baseScale = transform.localScale;

            FireTrigger(trigAlive);

            float t = 0f;
            while (t < aliveWiggleDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Sin((t / aliveWiggleDuration) * Mathf.PI * 2f);

                float dAngle = k * aliveRotateAmplitudeDeg;
                Quaternion wiggleRot = baseRot * Quaternion.Euler(0f, 0f, useZRotation ? dAngle : 0f);

                if (useZRotation)
                    transform.rotation = wiggleRot;
                else
                    transform.rotation = baseRot; // If you want 3D wiggle, change here

                if (aliveScalePulse > 0f)
                {
                    float s = 1f + (k * aliveScalePulse);
                    transform.localScale = baseScale * s;
                }

                transform.position = basePos; // hard-keep translation
                yield return null;
            }

            transform.position = basePos;
            transform.rotation = baseRot;
            transform.localScale = baseScale;

            FireTrigger(trigIdle);
            _aliveCo = null;
        }

        private IEnumerator MoveRoutine(Vector3 dest, bool recordPath, float speedMul, bool snapToInitialAtEnd = false)
        {
            float speed = Mathf.Max(0.0001f, moveSpeed * speedMul);
            float nextRecord = Time.time + pathRecordInterval;

            while (Vector3.Distance(transform.position, dest) > arriveThreshold)
            {
                Vector3 dir = (dest - transform.position);
                UpdateTurningState(dir);

                // Rotate toward direction
                Quaternion targetRot = ComputeFacingRotation(dir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotateSpeed * Time.deltaTime);

                // Translate
                Vector3 step = dir.normalized * speed * Time.deltaTime;

                // Prevent overshoot
                if (step.magnitude > dir.magnitude)
                    step = dir;

                transform.position += step;

                if (recordPath && Time.time >= nextRecord)
                {
                    nextRecord = Time.time + pathRecordInterval;
                    if (_recordedPath.Count < maxRecordedPoints)
                    {
                        // Only append if it meaningfully moved
                        if (Vector3.Distance(_recordedPath[_recordedPath.Count - 1], transform.position) > 0.005f)
                            _recordedPath.Add(transform.position);
                    }
                }

                yield return null;
            }

            // Snap final
            transform.position = dest;

            if (recordPath)
            {
                if (_recordedPath.Count < maxRecordedPoints)
                    _recordedPath.Add(dest);
            }

            if (snapToInitialAtEnd)
            {
                SnapToInitialPose();
            }

            FireTrigger(trigIdle);
            _motionCo = null;
        }

        private IEnumerator ReturnAlongRecordedPath()
        {
            // Traverse recorded points backward to follow "original path" in reverse.
            float speed = Mathf.Max(0.0001f, moveSpeed * returnSpeedMultiplier);

            for (int i = _recordedPath.Count - 1; i >= 0; i--)
            {
                Vector3 wp = _recordedPath[i];
                while (Vector3.Distance(transform.position, wp) > arriveThreshold)
                {
                    Vector3 dir = (wp - transform.position);
                    UpdateTurningState(dir);

                    Quaternion targetRot = ComputeFacingRotation(dir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotateSpeed * Time.deltaTime);

                    Vector3 step = dir.normalized * speed * Time.deltaTime;
                    if (step.magnitude > dir.magnitude)
                        step = dir;

                    transform.position += step;
                    yield return null;
                }

                transform.position = wp;
            }

            // Finally snap to exact original painting pose
            SnapToInitialPose();
            FireTrigger(trigIdle);

            _motionCo = null;
        }

        private IEnumerator InactivityWatchdog()
        {
            while (true)
            {
                // If nothing happens for a while, revert to initial state.
                if (Time.time - _lastUserInputTime >= inactivityReturnSeconds)
                {
                    // Only return if we aren't already at initial pose (allow tiny eps).
                    if (Vector3.Distance(transform.position, _initialPos) > 0.001f || Quaternion.Angle(transform.rotation, _initialRot) > 0.1f)
                    {
                        ReturnToOrigin();
                    }

                    // Prevent repeated triggering until next interaction
                    _lastUserInputTime = Time.time + 999999f;
                }

                yield return null;
            }
        }

        private void SnapToInitialPose()
        {
            transform.position = _initialPos;
            transform.rotation = _initialRot;
            transform.localScale = _initialScale;

            // Once reset, discard path so next return falls back to direct unless a new move happens
            _recordedPath.Clear();
            _recordedPath.Add(_initialPos);

            _lastTurnSign = 0;
        }

        private Quaternion ComputeFacingRotation(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-8f) return transform.rotation;

            float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + spriteForwardAngle;

            if (useZRotation)
                return Quaternion.Euler(0f, 0f, angleDeg);

            // 3D fallback: yaw on Y axis
            return Quaternion.Euler(0f, angleDeg, 0f);
        }

        private void UpdateTurningState(Vector3 dir)
        {
            // TurnLeft / TurnRight is triggered based on x-direction sign.
            // For a 2D ink painting, this corresponds to mirror-facing / turning cue.
            if (dir.sqrMagnitude < 1e-8f) return;

            int sign = (dir.x >= 0f) ? 1 : -1;
            if (sign == _lastTurnSign) return;

            _lastTurnSign = sign;

            if (sign > 0)
                FireTrigger(trigTurnRight);
            else
                FireTrigger(trigTurnLeft);
        }

        private void FireTrigger(string triggerName)
        {
            if (animator == null) return;
            if (string.IsNullOrEmpty(triggerName)) return;

            // Reset and set to avoid stuck triggers in certain Animator setups
            animator.ResetTrigger(triggerName);
            animator.SetTrigger(triggerName);
        }
    }
}

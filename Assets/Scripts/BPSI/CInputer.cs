// CInputer.cs
// BPSI - Brushstroke-Preserving Skeletal Interaction Method
// Input router: decides whether a click/tap hits a shrimp (Alive) or blank space (Move random 1–2 shrimps)
//
// Usage (typical):
// 1) Add this script to an empty GameObject (e.g., "BPSI_Input").
// 2) Assign the Camera (or leave empty to use Camera.main).
// 3) Make sure each shrimp has a Collider2D (recommended) or Collider, and a CMovement component.
// 4) Tag shrimp roots with "Shrimp" (default) OR put them on a dedicated Layer and set masks.

using System.Collections.Generic;
using UnityEngine;

namespace BPSI
{
    public class CInputer : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("If empty, Camera.main is used.")]
        public Camera targetCamera;

        [Tooltip("Shrimp agents. If empty and Auto Find is enabled, the script will populate on Start.")]
        public List<CMovement> shrimpAgents = new List<CMovement>();

        [Tooltip("Auto-find shrimp agents via FindObjectsOfType at Start.")]
        public bool autoFindShrimpsOnStart = true;

        [Header("Hit Detection")]
        [Tooltip("Tag used to identify shrimp roots (optional but recommended).")]
        public string shrimpTag = "Shrimp";

        [Tooltip("2D layer mask for shrimp colliders. Leave as Everything if you rely on Tag instead.")]
        public LayerMask shrimpLayer2D = ~0;

        [Tooltip("3D layer mask for shrimp colliders. Leave as Everything if you rely on Tag instead.")]
        public LayerMask shrimpLayer3D = ~0;

        [Header("Movement Trigger Rules")]
        [Tooltip("On blank click, randomly activate between these counts (clamped by available agents).")]
        [Range(1, 5)] public int minShrimpsToMove = 1;
        [Range(1, 5)] public int maxShrimpsToMove = 2;

        [Tooltip("Optional: constrain click targets into a rectangle in world space.")]
        public bool constrainToBounds = false;

        [Tooltip("World-space bounds (x,y,width,height). Only used when Constrain To Bounds is enabled.")]
        public Rect movementBounds = new Rect(-5f, -3f, 10f, 6f);

        [Tooltip("The Z-plane where clicks are interpreted (2D scene usually uses z=0).")]
        public float interactionPlaneZ = 0f;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;
        }

        private void Start()
        {
            if (autoFindShrimpsOnStart && (shrimpAgents == null || shrimpAgents.Count == 0))
            {
                var found = FindObjectsOfType<CMovement>();
                shrimpAgents = new List<CMovement>(found);
            }
        }

        private void Update()
        {
            if (targetCamera == null) return;

            // Touch (mobile)
            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began)
                {
                    HandlePointerDown(t.position);
                }
                return;
            }

            // Mouse (desktop)
            if (Input.GetMouseButtonDown(0))
            {
                HandlePointerDown(Input.mousePosition);
            }
        }

        private void HandlePointerDown(Vector2 screenPos)
        {
            // Notify all agents: this resets their inactivity timers
            BroadcastUserInteraction();

            Vector3 worldPoint = ScreenToWorldOnPlane(screenPos, interactionPlaneZ);
            if (constrainToBounds)
            {
                worldPoint.x = Mathf.Clamp(worldPoint.x, movementBounds.xMin, movementBounds.xMax);
                worldPoint.y = Mathf.Clamp(worldPoint.y, movementBounds.yMin, movementBounds.yMax);
            }

            // 1) Try 2D overlap first (best for sprite-based setups)
            Collider2D hit2D = Physics2D.OverlapPoint(worldPoint, shrimpLayer2D);
            if (hit2D != null)
            {
                var agent = hit2D.GetComponentInParent<CMovement>();
                if (agent != null && (string.IsNullOrEmpty(shrimpTag) || agent.CompareTag(shrimpTag)))
                {
                    agent.TriggerAlive();
                    return;
                }
            }

            // 2) Fallback to 3D raycast
            Ray ray = targetCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit3D, 1000f, shrimpLayer3D))
            {
                var agent = hit3D.collider.GetComponentInParent<CMovement>();
                if (agent != null && (string.IsNullOrEmpty(shrimpTag) || agent.CompareTag(shrimpTag)))
                {
                    agent.TriggerAlive();
                    return;
                }
            }

            // 3) Blank space -> random 1–2 shrimps to MoveTo target
            TriggerRandomMove(worldPoint);
        }

        private void TriggerRandomMove(Vector3 targetWorldPoint)
        {
            if (shrimpAgents == null || shrimpAgents.Count == 0)
                return;

            int available = shrimpAgents.Count;
            int count = Random.Range(minShrimpsToMove, maxShrimpsToMove + 1);
            count = Mathf.Clamp(count, 1, Mathf.Min(available, maxShrimpsToMove));

            // Shuffle indices
            List<int> idx = new List<int>(available);
            for (int i = 0; i < available; i++) idx.Add(i);
            for (int i = 0; i < available; i++)
            {
                int j = Random.Range(i, available);
                int tmp = idx[i];
                idx[i] = idx[j];
                idx[j] = tmp;
            }

            for (int k = 0; k < count; k++)
            {
                var agent = shrimpAgents[idx[k]];
                if (agent == null) continue;
                agent.MoveTo(targetWorldPoint);
            }
        }

        private void BroadcastUserInteraction()
        {
            if (shrimpAgents == null) return;
            for (int i = 0; i < shrimpAgents.Count; i++)
            {
                if (shrimpAgents[i] != null)
                    shrimpAgents[i].NotifyUserInteraction();
            }
        }

        private Vector3 ScreenToWorldOnPlane(Vector2 screenPos, float zPlane)
        {
            Ray ray = targetCamera.ScreenPointToRay(screenPos);
            Plane p = new Plane(Vector3.forward, new Vector3(0f, 0f, zPlane));
            if (p.Raycast(ray, out float enter))
            {
                return ray.GetPoint(enter);
            }

            // Fallback: approximate using ScreenToWorldPoint
            Vector3 wp = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(targetCamera.transform.position.z - zPlane)));
            wp.z = zPlane;
            return wp;
        }

        // Optional gizmo to visualize bounds
        private void OnDrawGizmosSelected()
        {
            if (!constrainToBounds) return;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireCube(new Vector3(movementBounds.center.x, movementBounds.center.y, interactionPlaneZ),
                                new Vector3(movementBounds.size.x, movementBounds.size.y, 0.01f));
        }
    }
}

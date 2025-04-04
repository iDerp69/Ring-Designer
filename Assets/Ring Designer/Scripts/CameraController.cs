using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine; // Ensure you have the Cinemachine namespace

namespace RingDesigner
{
    public class CameraController : MonoBehaviour
    {
        public InputActionReference Point;
        public InputActionReference Click;
        public InputActionReference MiddleClick;
        public InputActionReference RightClick;
        public InputActionReference ScrollWheel;

        public InputActionReference PrimaryTouchPosition;
        public InputActionReference SecondaryTouchPosition;

        [Header("Target and Orbit Settings")]
        public Transform target;
        public float distance = 10f;
        public float Sensitivity = 20f;          // Rotation sensitivity.
        public float SmoothTime = 0.1f;          // Smoothing time for orbit.
        [Range(-90f, 90f)]
        public float minPitch = -80f;
        [Range(-90f, 90f)]
        public float maxPitch = 80f;
        [Range(0.85f, 0.99f)]
        public float InertiaDamping = 0.95f;     // Inertia decay.

        [Header("Zoom Settings (Using Distance)")]
        public float TouchZoomSpeed = 0.1f;      // Touch zoom speed.
        public float scrollZoomSpeed = 1f;       // Mouse scroll zoom speed.
        public float minDistance = 5f;
        public float maxDistance = 20f;
        public float distanceSmoothTime = 0.1f;  // Smoothing time for zoom/distance.

        [Header("Pan Settings")]
        // Pan offset stored in view-space (x: right, y: up)
        public float panSpeed = 0.005f;
        public Vector2 panLimitMin = new Vector2(-0.5f, -0.5f);
        public Vector2 panLimitMax = new Vector2(0.5f, 0.5f);
        public float panSmoothTime = 0.1f;       // Smoothing time for panning.

        [Header("Cinemachine")]
        public CinemachineCamera cinemachineCamera;

        // ----- Internal State for Orbit -----
        // Target angles updated by input; current values are smoothed.
        private float targetYaw = 0f;
        private float targetPitch = 20f;
        private float currentYaw = 0f;
        private float currentPitch = 20f;
        // Inertia from pointer movement.
        private Vector2 inertiaVelocity = Vector2.zero;
        // Smoothing velocity (separate from inertia)
        private Vector2 rotationVelocity = Vector2.zero;
        private bool isOrbitDragging = false;
        private Vector2 lastOrbitPointer;

        // ----- Internal State for Pan -----
        // Pan offset stored in view-space (right, up)
        private Vector2 targetPan = Vector2.zero;
        private Vector2 currentPan = Vector2.zero;
        private Vector2 panVelocity = Vector2.zero;
        private bool isPanDragging = false;
        private Vector2 lastPanPointer;

        // ----- Internal State for Zoom (Distance) -----
        private float targetDistance;
        private float currentDistance;
        private float distanceVelocity = 0f;

        private bool inZoomAndPanMode;
        private Vector2 previousPrimaryTouchPosition = Vector2.zero;
        private Vector2 previousSecondaryTouchPosition = Vector2.zero;

        private Vector3 initialEulerAngles;

        void Start()
        {
            initialEulerAngles = transform.eulerAngles;
            if (target == null)
            {
                Debug.LogError("Target not assigned!");
                enabled = false;
                return;
            }
            Reinitialize();
        }

        public void Reinitialize()
        {
            targetYaw = default;
            targetPitch = 20f;
            currentYaw = default;
            currentPitch = 20f;
            inertiaVelocity = default;
            rotationVelocity = default;
            isOrbitDragging = default;
            lastOrbitPointer = default;
            targetPan = default;
            currentPan = default;
            panVelocity = default;
            isPanDragging = default;
            lastPanPointer = default;
            targetDistance = default;
            currentDistance = default;
            distanceVelocity = default;
            inZoomAndPanMode = default;
            previousPrimaryTouchPosition = default;
            previousSecondaryTouchPosition = default;

            // Initialize orbit from current camera rotation.
            Vector3 euler = initialEulerAngles;
            currentYaw = targetYaw = euler.y;
            currentPitch = targetPitch = euler.x;

            // Initialize distance from the starting value.
            targetDistance = currentDistance = distance;
        }

        void Update()
        {
            int touchCount = default;
            Vector2 primaryTouchPosition = PrimaryTouchPosition.action.ReadValue<Vector2>();
            if (primaryTouchPosition != Vector2.zero)
                touchCount++;
            Vector2 secondaryTouchPosition = SecondaryTouchPosition.action.ReadValue<Vector2>();
            if (secondaryTouchPosition != Vector2.zero)
                touchCount++;

            if (touchCount != 0)
                ProcessTouch();
            else
                ProcessMouse();
            Orbit();
            UpdateCamera();
            StorePrevious();


            void ProcessMouse()
            {
                inZoomAndPanMode = false;

                Vector2 pointerPos = Point.action.ReadValue<Vector2>();
                Vector2 scrollValue = ScrollWheel.action.ReadValue<Vector2>();
                bool leftClickIsPressed = Click.action.IsPressed();
                bool rightClickIsPressed = RightClick.action.IsPressed();
                bool middleClickIsPressed = MiddleClick.action.IsPressed();

                // --- Orbiting with left mouse button ---
                if (leftClickIsPressed)
                {
                    if (!isOrbitDragging)
                    {
                        isOrbitDragging = true;
                        lastOrbitPointer = pointerPos;
                        inertiaVelocity = Vector2.zero; // Reset inertia on new drag.
                    }
                    else
                    {
                        Vector2 delta = pointerPos - lastOrbitPointer;
                        targetYaw += delta.x * Sensitivity * 0.01f;
                        targetPitch -= delta.y * Sensitivity * 0.01f;
                        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
                        lastOrbitPointer = pointerPos;
                        inertiaVelocity = delta * (Sensitivity * 0.01f);
                    }
                }
                else
                {
                    isOrbitDragging = false;
                }

                // --- Panning with right or middle mouse button ---
                if (rightClickIsPressed || middleClickIsPressed)
                {
                    if (!isPanDragging)
                    {
                        isPanDragging = true;
                        lastPanPointer = pointerPos;
                    }
                    else
                    {
                        Vector2 panDelta = pointerPos - lastPanPointer;
                        // Update view-space pan offset.
                        targetPan.x = Mathf.Clamp(targetPan.x - panDelta.x * panSpeed, panLimitMin.x, panLimitMax.x);
                        targetPan.y = Mathf.Clamp(targetPan.y - panDelta.y * panSpeed, panLimitMin.y, panLimitMax.y);
                        lastPanPointer = pointerPos;
                    }
                }
                else
                {
                    isPanDragging = false;
                }

                // --- Zooming with mouse scroll wheel (changing camera distance) ---
                if (scrollValue.y != 0)
                {
                    targetDistance = Mathf.Clamp(targetDistance - scrollValue.y * scrollZoomSpeed, minDistance, maxDistance);
                }
            }

            void ProcessTouch()
            {
                if (touchCount == 1)
                {
                    if (inZoomAndPanMode)
                        return;

                    if (!isOrbitDragging)
                    {
                        isOrbitDragging = true;
                        lastOrbitPointer = primaryTouchPosition;
                        inertiaVelocity = Vector2.zero;
                    }
                    else
                    {
                        Vector2 delta = primaryTouchPosition - lastOrbitPointer;
                        targetYaw += delta.x * Sensitivity * 0.01f;
                        targetPitch -= delta.y * Sensitivity * 0.01f;
                        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
                        lastOrbitPointer = primaryTouchPosition;
                        inertiaVelocity = delta * (Sensitivity * 0.01f);
                    }
                }
                else if (touchCount >= 2 || inZoomAndPanMode)
                {
                    if (!inZoomAndPanMode)
                    {
                        inZoomAndPanMode = true;
                        // need to spend a frame getting previousSecondaryTouchPosition
                    }
                    else
                    {
                        // --- Pinch Zoom (update target distance) ---
                        float currentPinchDistance = Vector2.Distance(primaryTouchPosition, secondaryTouchPosition);
                        float lastPinchDistance = Vector2.Distance(previousPrimaryTouchPosition, previousSecondaryTouchPosition);
                        float deltaDistance = currentPinchDistance - lastPinchDistance;
                        targetDistance = Mathf.Clamp(targetDistance - deltaDistance * TouchZoomSpeed * 0.01f, minDistance, maxDistance);

                        // --- Two-finger Pan ---
                        Vector2 avgTouch = (primaryTouchPosition + secondaryTouchPosition) * 0.5f;
                        if (!isPanDragging)
                        {
                            isPanDragging = true;
                            lastPanPointer = avgTouch;
                        }
                        else
                        {
                            Vector2 panDelta = avgTouch - lastPanPointer;
                            targetPan.x = Mathf.Clamp(targetPan.x - panDelta.x * panSpeed, panLimitMin.x, panLimitMax.x);
                            targetPan.y = Mathf.Clamp(targetPan.y - panDelta.y * panSpeed, panLimitMin.y, panLimitMax.y);
                            lastPanPointer = avgTouch;
                        }
                    }

                }
                else
                {
                    // I don't think this is ever called actually because process touch relies on touchcount to not be 0
                    isOrbitDragging = false;
                    isPanDragging = false;
                }

            }

            void Orbit()
            {
                // If not orbit dragging, apply inertia.
                if (!isOrbitDragging)
                {
                    targetYaw += inertiaVelocity.x;
                    targetPitch -= inertiaVelocity.y;
                    targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
                    inertiaVelocity *= InertiaDamping;
                }

                // Smooth the orbit angles.
                currentYaw = Mathf.SmoothDamp(currentYaw, targetYaw, ref rotationVelocity.x, SmoothTime);
                currentPitch = Mathf.SmoothDamp(currentPitch, targetPitch, ref rotationVelocity.y, SmoothTime);
            }

            void UpdateCamera()
            {
                // Smooth pan.
                currentPan = Vector2.SmoothDamp(currentPan, targetPan, ref panVelocity, panSmoothTime);
                // Smooth zoom/distance.
                currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref distanceVelocity, distanceSmoothTime);

                // Compute rotation from current yaw and pitch.
                Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);
                // Compute world-space pan offset relative to current view.
                Vector3 panOffset = rotation * new Vector3(currentPan.x, currentPan.y, 0);
                // Compute new camera position using the current distance.
                Vector3 direction = rotation * new Vector3(0, 0, -currentDistance);
                Vector3 newTargetPos = target.position + panOffset;
                transform.position = newTargetPos + direction;
                transform.LookAt(newTargetPos);
            }

            void StorePrevious()
            {
                previousPrimaryTouchPosition = primaryTouchPosition;
                previousSecondaryTouchPosition = secondaryTouchPosition;
            }
        }

        

        


    }
}

/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using UnityEngine;
using Oculus.Interaction;
using UnityEngine.Events;

namespace Oculus.Interaction
{
    public class ResettingOneGrabRotateTransformer : MonoBehaviour, ITransformer
    {
        public enum Axis
        {
            Right = 0,
            Up = 1,
            Forward = 2
        }

        [SerializeField, Optional]
        private Transform _pivotTransform = null;

        public Transform Pivot => _pivotTransform != null ? _pivotTransform : transform;

        [SerializeField]
        private Axis _rotationAxis = Axis.Up;

        public Axis RotationAxis => _rotationAxis;

        [Serializable]
        public class OneGrabRotateConstraints
        {
            public FloatConstraint MinAngle;
            public FloatConstraint MaxAngle;
        }

        [SerializeField]
        private OneGrabRotateConstraints _constraints =
            new OneGrabRotateConstraints()
            {
                MinAngle = new FloatConstraint(),
                MaxAngle = new FloatConstraint()
            };

        public OneGrabRotateConstraints Constraints
        {
            get => _constraints;
            set => _constraints = value;
        }

        // no longer used for clamping logic, but kept if something external reads them
        private float _relativeAngle = 0.0f;
        private float _constrainedRelativeAngle = 0.0f;

        private IGrabbable _grabbable;
        private Vector3 _grabPositionInPivotSpace;
        private Pose _transformPoseInPivotSpace;

        private Pose _worldPivotPose;
        private Vector3 _previousVectorInPivotSpace;

        private Quaternion _localRotation;
        private float _startAngle = 0;

        // NEW: remember the dial's original "zero" local rotation
        private Quaternion _zeroLocalRotation;
        private bool _zeroRotationInitialized = false;

        //[SerializeField]
        //private float _returnSpeed = 180f; // degrees per second for snapping back

        [Header("Return / Snap Settings")]
        [SerializeField]
        private bool _autoSnapOnRelease = true;

        [SerializeField]
        private float _snapSpeed = 360f; // deg/sec

        [SerializeField]
        private float[] _snapAngles = new float[] { -90f, 0f, 90f }; // dial options in degrees

        private bool _isSnapping = false;
        private Quaternion _snapTargetLocalRotation;
        private int _snapOption = 0;

        public UnityEvent<int> OnSnappedToOption;

        public void Initialize(IGrabbable grabbable)
        {
            _grabbable = grabbable;
        }

        public Pose ComputeWorldPivotPose()
        {
            if (_pivotTransform != null)
            {
                return _pivotTransform.GetPose();
            }

            var targetTransform = _grabbable.Transform;

            Vector3 worldPosition = targetTransform.position;
            Quaternion worldRotation = targetTransform.parent != null
                ? targetTransform.parent.rotation * _localRotation
                : _localRotation;

            return new Pose(worldPosition, worldRotation);
        }

        public void BeginTransform()
        {
            var grabPoint = _grabbable.GrabPoints[0];
            var targetTransform = _grabbable.Transform;

            if (_pivotTransform == null)
            {
                _localRotation = targetTransform.localRotation;
            }

            // Initialize the "zero" local rotation once, at startup (or the first grab)
            if (!_zeroRotationInitialized)
            {
                _zeroLocalRotation = targetTransform.localRotation;
                _zeroRotationInitialized = true;
            }

            Vector3 localAxis = Vector3.zero;
            localAxis[(int)_rotationAxis] = 1f;

            _worldPivotPose = ComputeWorldPivotPose();
            Vector3 rotationAxis = _worldPivotPose.rotation * localAxis;

            Quaternion inverseRotation = Quaternion.Inverse(_worldPivotPose.rotation);

            Vector3 grabDelta = grabPoint.position - _worldPivotPose.position;
            if (Mathf.Abs(grabDelta.magnitude) < 0.001f)
            {
                Vector3 localAxisNext = Vector3.zero;
                localAxisNext[((int)_rotationAxis + 1) % 3] = 0.001f;
                grabDelta = _worldPivotPose.rotation * localAxisNext;
            }

            _grabPositionInPivotSpace = inverseRotation * grabDelta;

            Vector3 worldPositionDelta =
                inverseRotation * (targetTransform.position - _worldPivotPose.position);

            Quaternion worldRotationDelta = inverseRotation * targetTransform.rotation;
            _transformPoseInPivotSpace = new Pose(worldPositionDelta, worldRotationDelta);

            Vector3 initialOffset = _worldPivotPose.rotation * _grabPositionInPivotSpace;
            Vector3 initialVector = Vector3.ProjectOnPlane(initialOffset, rotationAxis);
            _previousVectorInPivotSpace = Quaternion.Inverse(_worldPivotPose.rotation) * initialVector;

            _startAngle = _constrainedRelativeAngle;
            _relativeAngle = _startAngle;

            float parentScale = targetTransform.parent != null ? targetTransform.parent.lossyScale.x : 1f;
            _transformPoseInPivotSpace.position /= parentScale;
        }

        public void UpdateTransform()
        {
            var grabPoint = _grabbable.GrabPoints[0];
            var targetTransform = _grabbable.Transform;

            Vector3 localAxis = Vector3.zero;
            localAxis[(int)_rotationAxis] = 1f;
            _worldPivotPose = ComputeWorldPivotPose();
            Vector3 rotationAxis = _worldPivotPose.rotation * localAxis;

            // Original relative rotation computation (still used to get an unconstrained rotation)
            Vector3 targetOffset = grabPoint.position - _worldPivotPose.position;
            Vector3 targetVector = Vector3.ProjectOnPlane(targetOffset, rotationAxis);

            Vector3 previousVectorInWorldSpace =
                _worldPivotPose.rotation * _previousVectorInPivotSpace;

            _previousVectorInPivotSpace = Quaternion.Inverse(_worldPivotPose.rotation) * targetVector;

            float signedAngle =
                Vector3.SignedAngle(previousVectorInWorldSpace, targetVector, rotationAxis);

            _relativeAngle += signedAngle;

            _constrainedRelativeAngle = _relativeAngle;

            Quaternion deltaRotation = Quaternion.AngleAxis(_constrainedRelativeAngle - _startAngle, rotationAxis);

            float parentScale = targetTransform.parent != null ? targetTransform.parent.lossyScale.x : 1f;
            Pose transformDeltaInWorldSpace =
                new Pose(
                    _worldPivotPose.rotation * (parentScale * _transformPoseInPivotSpace.position),
                    _worldPivotPose.rotation * _transformPoseInPivotSpace.rotation);

            Pose transformDeltaRotated = new Pose(
                deltaRotation * transformDeltaInWorldSpace.position,
                deltaRotation * transformDeltaInWorldSpace.rotation);

            // This is the unconstrained world rotation from the grab
            Quaternion unconstrainedWorldRot = transformDeltaRotated.rotation;

            // Convert that into local space
            Quaternion parentRot = targetTransform.parent != null
                ? targetTransform.parent.rotation
                : Quaternion.identity;
            Quaternion unconstrainedLocalRot = Quaternion.Inverse(parentRot) * unconstrainedWorldRot;

            // Compute absolute signed angle around the selected local axis,
            // relative to the dial's zero local rotation.
            Quaternion deltaFromZero = Quaternion.Inverse(_zeroLocalRotation) * unconstrainedLocalRot;
            Vector3 deltaEuler = deltaFromZero.eulerAngles;

            float absAngle;
            switch (_rotationAxis)
            {
                case Axis.Right:
                    absAngle = NormalizeAngle(deltaEuler.x);
                    break;
                case Axis.Up:
                    absAngle = NormalizeAngle(deltaEuler.y);
                    break;
                default: // Forward
                    absAngle = NormalizeAngle(deltaEuler.z);
                    break;
            }

            // Clamp this absolute angle to the desired range (e.g., -90 to 90)
            float clampedAngle = absAngle;
            if (Constraints.MinAngle.Constrain)
            {
                clampedAngle = Mathf.Max(clampedAngle, Constraints.MinAngle.Value);
            }
            if (Constraints.MaxAngle.Constrain)
            {
                clampedAngle = Mathf.Min(clampedAngle, Constraints.MaxAngle.Value);
            }

            // Rebuild a local rotation that is zeroLocalRotation plus the clamped angle around the chosen axis
            Quaternion clampedDelta;
            switch (_rotationAxis)
            {
                case Axis.Right:
                    clampedDelta = Quaternion.AngleAxis(clampedAngle, Vector3.right);
                    break;
                case Axis.Up:
                    clampedDelta = Quaternion.AngleAxis(clampedAngle, Vector3.up);
                    break;
                default:
                    clampedDelta = Quaternion.AngleAxis(clampedAngle, Vector3.forward);
                    break;
            }

            Quaternion clampedLocalRot = _zeroLocalRotation * clampedDelta;
            Quaternion clampedWorldRot = parentRot * clampedLocalRot;

            // Apply constrained pose: keep the grabbed position behavior, but clamp rotation
            targetTransform.position = _worldPivotPose.position + transformDeltaRotated.position;
            targetTransform.rotation = clampedWorldRot;
        }

        public void EndTransform() {
            //StartReturnToZero();
            if (_autoSnapOnRelease)
            {
                StartSnapToNearest();
            }
        }

        public void StartSnapToIndex(int index)
        {
            if (!_zeroRotationInitialized || _grabbable == null || _snapAngles == null || _snapAngles.Length == 0)
                return;

            var targetTransform = _grabbable.Transform;

            // Current local rotation
            Quaternion currentLocal = targetTransform.localRotation;

            // Compute current absolute angle around chosen axis relative to zero
            Quaternion deltaFromZero = Quaternion.Inverse(_zeroLocalRotation) * currentLocal;
            Vector3 deltaEuler = deltaFromZero.eulerAngles;

            float currentAngle;
            switch (_rotationAxis)
            {
                case Axis.Right:
                    currentAngle = NormalizeAngle(deltaEuler.x);
                    break;
                case Axis.Up:
                    currentAngle = NormalizeAngle(deltaEuler.y);
                    break;
                default:
                    currentAngle = NormalizeAngle(deltaEuler.z);
                    break;
            }

            // Find nearest snap angle
            int bestIndex = index;
            float bestAngle = _snapAngles[bestIndex];
            float bestDist = Mathf.Abs(NormalizeAngle(currentAngle - bestAngle));
            /*for (int i = 1; i < _snapAngles.Length; i++)
            {
                float dist = Mathf.Abs(NormalizeAngle(currentAngle - _snapAngles[i]));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIndex = i;
                    bestAngle = _snapAngles[i];
                }
            }*/

            // Build target local rotation = zeroLocalRotation * rotation(bestAngle)
            Quaternion snapDelta;
            switch (_rotationAxis)
            {
                case Axis.Right:
                    snapDelta = Quaternion.AngleAxis(bestAngle, Vector3.right);
                    break;
                case Axis.Up:
                    snapDelta = Quaternion.AngleAxis(bestAngle, Vector3.up);
                    break;
                default:
                    snapDelta = Quaternion.AngleAxis(bestAngle, Vector3.forward);
                    break;
            }

            _snapTargetLocalRotation = _zeroLocalRotation * snapDelta;
            _snapOption = bestIndex;
            _isSnapping = true;
        }

        /// <summary>
        /// Call this to snap the dial to the nearest allowed angle from _snapAngles.
        /// </summary>
        public void StartSnapToNearest()
        {
            if (!_zeroRotationInitialized || _grabbable == null || _snapAngles == null || _snapAngles.Length == 0)
                return;

            var targetTransform = _grabbable.Transform;

            // Current local rotation
            Quaternion currentLocal = targetTransform.localRotation;

            // Compute current absolute angle around chosen axis relative to zero
            Quaternion deltaFromZero = Quaternion.Inverse(_zeroLocalRotation) * currentLocal;
            Vector3 deltaEuler = deltaFromZero.eulerAngles;

            float currentAngle;
            switch (_rotationAxis)
            {
                case Axis.Right:
                    currentAngle = NormalizeAngle(deltaEuler.x);
                    break;
                case Axis.Up:
                    currentAngle = NormalizeAngle(deltaEuler.y);
                    break;
                default:
                    currentAngle = NormalizeAngle(deltaEuler.z);
                    break;
            }

            // Find nearest snap angle
            int bestIndex = 0;
            float bestAngle = _snapAngles[bestIndex];
            float bestDist = Mathf.Abs(NormalizeAngle(currentAngle - bestAngle));
            for (int i = 1; i < _snapAngles.Length; i++)
            {
                float dist = Mathf.Abs(NormalizeAngle(currentAngle - _snapAngles[i]));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIndex = i;
                    bestAngle = _snapAngles[i];
                }
            }

            // Build target local rotation = zeroLocalRotation * rotation(bestAngle)
            Quaternion snapDelta;
            switch (_rotationAxis)
            {
                case Axis.Right:
                    snapDelta = Quaternion.AngleAxis(bestAngle, Vector3.right);
                    break;
                case Axis.Up:
                    snapDelta = Quaternion.AngleAxis(bestAngle, Vector3.up);
                    break;
                default:
                    snapDelta = Quaternion.AngleAxis(bestAngle, Vector3.forward);
                    break;
            }

            _snapTargetLocalRotation = _zeroLocalRotation * snapDelta;
            _snapOption = bestIndex;
            _isSnapping = true;
        }


        #region Inject

        public void InjectOptionalPivotTransform(Transform pivotTransform)
        {
            _pivotTransform = pivotTransform;
        }

        public void InjectOptionalRotationAxis(Axis rotationAxis)
        {
            _rotationAxis = rotationAxis;
        }

        public void InjectOptionalConstraints(OneGrabRotateConstraints constraints)
        {
            _constraints = constraints;
        }

        #endregion

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            return angle;
        }

        /*public void StartReturnToZero()
        {
            if (!_zeroRotationInitialized) return;
            _isReturning = true;
        }*/

        private void Update()
        {
            /*if (!_isReturning || _grabbable == null) return;

            var targetTransform = _grabbable.Transform;

            // current local rotation
            Quaternion currentLocal = targetTransform.localRotation;

            // rotate towards zeroLocalRotation at a fixed angular speed
            float maxStep = _returnSpeed * Time.deltaTime;
            Quaternion nextLocal = Quaternion.RotateTowards(
                currentLocal,
                _zeroLocalRotation,
                maxStep
            );

            targetTransform.localRotation = nextLocal;

            // stop when we’ve essentially reached zero
            if (Quaternion.Angle(nextLocal, _zeroLocalRotation) < 0.1f)
            {
                targetTransform.localRotation = _zeroLocalRotation;
                _isReturning = false;
            }*/
            if (!_isSnapping || _grabbable == null) return;

            var targetTransform = _grabbable.Transform;

            Quaternion currentLocal = targetTransform.localRotation;
            float maxStep = _snapSpeed * Time.deltaTime;

            // Smoothly rotate local rotation toward the snap target
            Quaternion nextLocal = Quaternion.RotateTowards(
                currentLocal,
                _snapTargetLocalRotation,
                maxStep
            );

            targetTransform.localRotation = nextLocal;

            if (Quaternion.Angle(nextLocal, _snapTargetLocalRotation) < 0.1f)
            {
                targetTransform.localRotation = _snapTargetLocalRotation;
                _isSnapping = false;
                OnSnappedToOption?.Invoke(_snapOption);
            }
        }

        /*private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            return angle;
        }*/
    }
}


/// <summary>
/// A Transformer that rotates the target about an axis.
/// Updates apply relative rotational changes of a GrabPoint about an axis.
/// The axis is defined by a pivot transform: a world position and up vector.
/// </summary>
/*public class ResettingOneGrabRotateTransformer : MonoBehaviour, ITransformer
{
    public enum Axis
    {
        Right = 0,
        Up = 1,
        Forward = 2
    }

    [SerializeField, Optional]
    private Transform _pivotTransform = null;

    public Transform Pivot => _pivotTransform != null ? _pivotTransform : transform;

    [SerializeField]
    private Axis _rotationAxis = Axis.Up;

    public Axis RotationAxis => _rotationAxis;

    [Serializable]
    public class OneGrabRotateConstraints
    {
        public FloatConstraint MinAngle;
        public FloatConstraint MaxAngle;
    }

    [SerializeField]
    private OneGrabRotateConstraints _constraints =
        new OneGrabRotateConstraints()
        {
            MinAngle = new FloatConstraint(),
            MaxAngle = new FloatConstraint()
        };

    public OneGrabRotateConstraints Constraints
    {
        get
        {
            return _constraints;
        }

        set
        {
            _constraints = value;
        }
    }

    public bool RelativeConstraints = false;

    // TODO: change to absolute angles
    protected float _relativeAngle = 0.0f;
    protected float _constrainedRelativeAngle = 0.0f;

    private IGrabbable _grabbable;
    private Vector3 _grabPositionInPivotSpace;
    private Pose _transformPoseInPivotSpace;

    private Pose _worldPivotPose;
    private Pose _originalLocalPose;
    private Vector3 _previousVectorInPivotSpace;

    protected Quaternion _localRotation;
    protected float _startAngle = 0;

    protected virtual void Start()
    {
        _originalLocalPose = ComputeLocalPose();
    }

    protected virtual void OnEnable()
    {
        InteractableUnityEventWrapper wrapper = GetComponent<InteractableUnityEventWrapper>();
        wrapper.WhenUnselect.AddListener(ResetTransform);
    }

    protected virtual void OnDisable()
    {
        InteractableUnityEventWrapper wrapper = GetComponent<InteractableUnityEventWrapper>();
        wrapper.WhenUnselect.RemoveListener(ResetTransform);
    }

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
    }

    public Pose ComputeLocalPose()
    {
        if (_pivotTransform != null)
        {
            return _pivotTransform.GetPose(Space.Self);
        }

        var targetTransform = _grabbable.Transform;
        Vector3 localPosition = targetTransform.localPosition;
        Quaternion localRotation = targetTransform.localRotation;
        return new Pose(localPosition, localRotation);
    }

    public Pose ComputeWorldPivotPose()
    {
        if (_pivotTransform != null)
        {
            return _pivotTransform.GetPose();
        }

        var targetTransform = _grabbable.Transform;

        Vector3 worldPosition = targetTransform.position;
        Quaternion worldRotation = targetTransform.parent != null
            ? targetTransform.parent.rotation * _localRotation
            : _localRotation;

        return new Pose(worldPosition, worldRotation);
    }

    public void BeginTransform()
    {
        var grabPoint = _grabbable.GrabPoints[0];
        var targetTransform = _grabbable.Transform;

        if (_pivotTransform == null)
        {
            _localRotation = targetTransform.localRotation;
        }

        Vector3 localAxis = Vector3.zero;
        localAxis[(int)_rotationAxis] = 1f;

        _worldPivotPose = ComputeWorldPivotPose();
        Vector3 rotationAxis = _worldPivotPose.rotation * localAxis;

        Quaternion inverseRotation = Quaternion.Inverse(_worldPivotPose.rotation);

        Vector3 grabDelta = grabPoint.position - _worldPivotPose.position;
        // The initial delta must be non-zero between the pivot and grab location for rotation
        if (Mathf.Abs(grabDelta.magnitude) < 0.001f)
        {
            Vector3 localAxisNext = Vector3.zero;
            localAxisNext[((int)_rotationAxis + 1) % 3] = 0.001f;
            grabDelta = _worldPivotPose.rotation * localAxisNext;
        }

        _grabPositionInPivotSpace =
            inverseRotation * grabDelta;

        Vector3 worldPositionDelta =
            inverseRotation * (targetTransform.position - _worldPivotPose.position);

        Quaternion worldRotationDelta = inverseRotation * targetTransform.rotation;
        _transformPoseInPivotSpace = new Pose(worldPositionDelta, worldRotationDelta);

        Vector3 initialOffset = _worldPivotPose.rotation * _grabPositionInPivotSpace;
        Vector3 initialVector = Vector3.ProjectOnPlane(initialOffset, rotationAxis);
        _previousVectorInPivotSpace = Quaternion.Inverse(_worldPivotPose.rotation) * initialVector;

        _startAngle = _constrainedRelativeAngle;
        _relativeAngle = _startAngle;

        float parentScale = targetTransform.parent != null ? targetTransform.parent.lossyScale.x : 1f;
        _transformPoseInPivotSpace.position /= parentScale;
    }

    public void UpdateTransform()
    {
        var grabPoint = _grabbable.GrabPoints[0];
        var targetTransform = _grabbable.Transform;

        Vector3 localAxis = Vector3.zero;
        localAxis[(int)_rotationAxis] = 1f;
        _worldPivotPose = ComputeWorldPivotPose();
        Vector3 rotationAxis = _worldPivotPose.rotation * localAxis;

        // Project our positional offsets onto a plane with normal equal to the rotation axis
        Vector3 targetOffset = grabPoint.position - _worldPivotPose.position;
        Vector3 targetVector = Vector3.ProjectOnPlane(targetOffset, rotationAxis);

        // same reference direction as in BeginTransform (pivot forward projected)
        //Vector3 pivotRef = Vector3.ProjectOnPlane(_originalPivotPose.rotation * Vector3.forward, rotationAxis);
        //float currentAbsoluteAngle = Vector3.SignedAngle(pivotRef, targetVector, rotationAxis);

        

        Vector3 previousVectorInWorldSpace =
            _worldPivotPose.rotation * _previousVectorInPivotSpace;

        // update previous
        _previousVectorInPivotSpace = Quaternion.Inverse(_worldPivotPose.rotation) * targetVector;

        float signedAngle =
            Vector3.SignedAngle(previousVectorInWorldSpace, targetVector, rotationAxis);

        _relativeAngle += signedAngle;

        _constrainedRelativeAngle = _relativeAngle;

        Debug.Log($"Current local angle: {_localRotation}, Relative Angle: {_relativeAngle}");

        // TODO: We want to constrain the absolute angle, not the relative one


        if (Constraints.MinAngle.Constrain)
        {
            _constrainedRelativeAngle = Mathf.Max(_constrainedRelativeAngle, Constraints.MinAngle.Value);
        }
        if (Constraints.MaxAngle.Constrain)
        {
            _constrainedRelativeAngle = Mathf.Min(_constrainedRelativeAngle, Constraints.MaxAngle.Value);
        }

        Quaternion deltaRotation = Quaternion.AngleAxis(_constrainedRelativeAngle - _startAngle, rotationAxis);

        float parentScale = targetTransform.parent != null ? targetTransform.parent.lossyScale.x : 1f;
        Pose transformDeltaInWorldSpace =
            new Pose(
                _worldPivotPose.rotation * (parentScale * _transformPoseInPivotSpace.position),
                _worldPivotPose.rotation * _transformPoseInPivotSpace.rotation);

        Pose transformDeltaRotated = new Pose(
            deltaRotation * transformDeltaInWorldSpace.position,
            deltaRotation * transformDeltaInWorldSpace.rotation);

        targetTransform.position = _worldPivotPose.position + transformDeltaRotated.position;
        targetTransform.rotation = transformDeltaRotated.rotation;
    }

    public void ResetTransform()
    {
        Debug.Log("RESETTING TRANSFORM");
        _constrainedRelativeAngle = 0.0f;
        _relativeAngle = 0.0f;
        _startAngle = 0.0f;
    }


    public virtual void EndTransform() { }

    #region Inject

    public void InjectOptionalPivotTransform(Transform pivotTransform)
    {
        _pivotTransform = pivotTransform;
    }

    public void InjectOptionalRotationAxis(Axis rotationAxis)
    {
        _rotationAxis = rotationAxis;
    }

    public void InjectOptionalConstraints(OneGrabRotateConstraints constraints)
    {
        _constraints = constraints;
    }

    #endregion
}*/
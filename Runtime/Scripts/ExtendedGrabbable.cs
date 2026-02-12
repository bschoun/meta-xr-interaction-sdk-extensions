using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// Adds additional functionality to the Grabbable class
/// </summary>
namespace InteractionExtensions
{
    public class ExtendedGrabbable : Grabbable
    {
        //public bool isThrowable = true;
        /// <summary>
        /// Force unselect of the grabbable
        /// </summary>
        public void ForceUnselect()
        {
            //Debug.Log($"FORCE UNSELECT for {name}");
            List<int> selectingIds = _selectingPointIds;
            if (selectingIds == null) return;
            for (int i = 0; i < selectingIds.Count; i++)
            {
                ProcessPointerEvent(new PointerEvent(selectingIds[i], PointerEventType.Unselect, Pose.identity));
            }
        }

        /// <summary>
        /// This overrides (and then calls) the base class PointableElementUpdated.
        /// We want to basically catch the select event so that we can disable isKinematic on the rigidbody
        /// before the rigidbody is locked in the base class.
        /// 
        /// In a practical sense - without calling this, when you grab an object that has isKinematic == true, 
        /// the first throw of the object will not have any velocity applied - it will drop to the floor.
        /// This is why we intercept the select event, make the object non-kinematic, and then let Meta handle the
        /// rest of the grabbing/throwing.
        /// </summary>
        /// <param name="evt"></param>
        protected override void PointableElementUpdated(PointerEvent evt)
        {

            // We can only get the _throwWhenUnselected field value from reflection, because this field is private
            // Normally shouldn't use reflection, but Meta makes my life difficult sometimes, so... here we are
            bool? isThrowable = Utils.GetInstanceField(typeof(Grabbable), this, "_throwWhenUnselected") as bool?;
            if (isThrowable != null)
            {
                if (evt.Type == PointerEventType.Select && (bool)isThrowable)
                {
                    Rigidbody rb = GetComponent<Rigidbody>();
                    if (rb != null && rb.isKinematic)
                    {
                        rb.isKinematic = false;
                    }
                }
            }
            else
            {
                Debug.LogWarning("Could not get _throwWhenUnselected field of Grabbable base class, check Meta's documentation.");
            }
            base.PointableElementUpdated(evt);
        }



        protected override void OnDisable()
        {
            base.OnDisable();
            ForceUnselect();
        }
    }
}
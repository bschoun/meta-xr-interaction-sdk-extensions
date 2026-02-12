using Oculus.Interaction;
using Oculus.Interaction.Collections;
using Oculus.Interaction.HandGrab;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace InteractionExtensions
{
    public static class Utils
    {
        /// <summary>
        /// Enables/disables rendering of GameObject "go" and all its children.
        /// </summary>
        /// <param name="go"></param>
        /// <param name="enabled"></param>
        public static void SetRenderers(GameObject go, bool enabled)
        {
            if (go == null) return;
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                r.enabled = enabled;
            }
        }

        public static void SetInteractables(GameObject go, bool enabled, bool recursive = true)
        {
            DistanceGrabInteractable dginteractable = go.GetComponent<DistanceGrabInteractable>();
            if (dginteractable != null)
            {
                //if (!enabled)
                //{
                //    DistanceGrabInteractable.Registry.Unregister(dginteractable);
                //}
                dginteractable.enabled = enabled;
            }

            DistanceHandGrabInteractable dhginteractable = go.GetComponent<DistanceHandGrabInteractable>();
            if (dhginteractable != null)
            {
                //if (!enabled)
                //{
                //    DistanceHandGrabInteractable.Registry.Unregister(dhginteractable);
                //}
                dhginteractable.enabled = enabled;
            }

            GrabInteractable ginteractable = go.GetComponent<GrabInteractable>();
            if (ginteractable != null)
            {
                //if (!enabled)
                //{
                //    GrabInteractable.Registry.Unregister(ginteractable);
                //}
                ginteractable.enabled = enabled;
            }

            HandGrabInteractable hginteractable = go.GetComponent<HandGrabInteractable>();
            if (hginteractable != null)
            {
                //if (!enabled)
                //{
                //    HandGrabInteractable.Registry.Unregister(hginteractable);
                //}
                hginteractable.enabled = enabled;
            }


            PokeInteractable pinteractable = go.GetComponent<PokeInteractable>();
            if (pinteractable != null)
            {
                //if (!enabled)
                //{
                //    PokeInteractable.Registry.Unregister(pinteractable);
                //}
                //pinteractable.enabled = enabled;
            }


            // Force player to release the object if they're holding it
            ExtendedGrabbable grabbable = go.GetComponent<ExtendedGrabbable>();
            if (grabbable != null)
            {
                grabbable.ForceUnselect();
            }

            if (recursive)
            {
                foreach (Transform t in go.transform)
                {
                    SetInteractables(t.gameObject, enabled);
                }
            }
        }

        public static List<Transform> GetChildRigidbodyTransforms(Transform parent)
        {
            List<Transform> result = new List<Transform>();
            Rigidbody[] rbs = parent.GetComponentsInChildren<Rigidbody>();
            foreach (var r in rbs)
            {
                if (r.transform != parent)
                    result.Add(r.transform);
            }
            return result;
        }

        /// <summary>
        /// Uses reflection to get the field value from an object.
        /// </summary>
        ///
        /// <param name="type">The instance type.</param>
        /// <param name="instance">The instance object.</param>
        /// <param name="fieldName">The field's name which is to be fetched.</param>
        ///
        /// <returns>The field value from the object.</returns>
        public static object GetInstanceField(Type type, object instance, string fieldName)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static;
            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field.GetValue(instance);
        }
    }
}
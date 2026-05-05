// SPDX-License-Identifier: MIT
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace EyeLean.Skeleton
{
    [ExecuteInEditMode]
    public class VisionConeScript : MonoBehaviour
    {
        public float angle = 60;
        public float minVisionRadius = 1;
        public float maxVisionRadius = 2;
        public bool showDebugLine = false;

        private SphereCollider sphereCollider = null;
        public CapsuleCollider capCollider = null;
        private BoxCollider boxCollider = null;

        // VELOCITY
        private Vector3 velocity;
        private Vector3 prevPosition;

        // TIME TO COLLISION
        public float TTCThreshold = 1f;
        public float stopDuration = 0.5f;
        private float stopTime = -10;
        public bool STOP = false;

        // VISUALIZATION
        private int segments = 20;

        private float minDetectedDist;

        // Cache GetComponentInChildren lookups across physics frames
        private static Dictionary<GameObject, VisionConeScript> visionConeCache = new Dictionary<GameObject, VisionConeScript>();

        void Start()
        {
            var gap = GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Gap");
            if (gap == null)
            {
                gap = new GameObject("Gap").transform;
                gap.parent = this.transform;
                gap.transform.localPosition = Vector3.zero;
                boxCollider = gap.gameObject.AddComponent<BoxCollider>();
                boxCollider.isTrigger = true;
            } else
            {
                boxCollider = gap.GetComponent<BoxCollider>();
            }
            gap.transform.localPosition = Vector3.zero;
            boxCollider.enabled = false;

            sphereCollider = GetComponent<SphereCollider>();
            if (sphereCollider == null)
            {
                sphereCollider = gameObject.AddComponent<SphereCollider>();
                sphereCollider.isTrigger = true;
            }
            sphereCollider.hideFlags = HideFlags.HideInHierarchy;

            capCollider = GetComponent<CapsuleCollider>();
            if (capCollider == null)
            {
                capCollider = gameObject.AddComponent<CapsuleCollider>();
                capCollider.isTrigger = true;
            }
            Vector3 boxSize = Vector3.zero;
            boxSize.x = capCollider.radius * 2;
            boxSize.y = capCollider.height;
            boxSize.z = capCollider.radius * 2;
            boxCollider.size = boxSize;
            boxCollider.center = capCollider.center;

            prevPosition = transform.position;
            if (transform.parent.GetComponent<Rigidbody>() == null)
            {
                Debug.LogWarning("If you want " + transform.parent.name + " to be detected by a Vision Cone, add a RigidBody to " + transform.parent.name + ".");
            }
        }

        void FixedUpdate()
        {
            velocity = (transform.position - prevPosition) / Time.fixedDeltaTime;

            prevPosition = transform.position;

            boxCollider.enabled = false;
            minDetectedDist = float.PositiveInfinity;
        }

        private void Update()
        {
            sphereCollider.radius = maxVisionRadius;
            if (minVisionRadius >= maxVisionRadius)
            {
                Debug.LogError("Min radius of vision cone is not less than max radius!");
            }

            if (Time.time < stopTime + stopDuration)
                STOP = true;
            else
                STOP = false;
        }

        private void OnTriggerStay(Collider other)
        {
            Detect(other);
        }

        private void Detect(Collider other)
        {
            var angleDiff = Vector3.Angle(other.gameObject.transform.position - transform.position, transform.forward);
            var dist = Vector3.Distance(other.gameObject.transform.position, transform.position);

            VisionConeScript otherVisionCone = null;
            GameObject rootObj = other.transform.root.gameObject;
            if (!visionConeCache.TryGetValue(rootObj, out otherVisionCone))
            {
                otherVisionCone = rootObj.GetComponentInChildren<VisionConeScript>();
                visionConeCache[rootObj] = otherVisionCone; // cache null too
            }

            bool isParticipant = IsParticipant(other);

            // Detect agents (which carry their own vision cone) and the participant
            bool shouldDetect = (other.isTrigger && otherVisionCone != null) || isParticipant;
            
            if (shouldDetect && angleDiff <= angle && dist <= maxVisionRadius)
            {
                if (showDebugLine && angleDiff < 20 && dist > capCollider.radius * 2)
                {
                    if (dist < minDetectedDist)
                    {
                        minDetectedDist = dist;

                        Debug.DrawLine(transform.position, other.transform.position, Color.yellow);

                        boxCollider.enabled = true;
                        boxCollider.gameObject.transform.position = (transform.position + other.transform.position) / 2f;
                        var boxSize = boxCollider.size;
                        boxSize.z = dist - capCollider.radius * 2;
                        boxCollider.size = boxSize;
                    }
                    else
                    {
                        return;
                    }
                } else
                {
                    boxCollider.enabled = false;
                    boxCollider.gameObject.transform.localPosition = Vector3.zero;
                }

                var selfVelocity = GetVelocity();
                var otherVelocity = otherVisionCone != null ? otherVisionCone.GetVelocity() : Vector3.zero;
                var TTC = TimeToCollision(
                    new Vector2(transform.position.x, transform.position.z),
                    new Vector2(selfVelocity.x, selfVelocity.z),
                    capCollider.radius,
                    new Vector2(other.transform.position.x, other.transform.position.z),
                    new Vector2(otherVelocity.x, otherVelocity.z),
                    capCollider.radius
                );

                if (TTC > 0 && TTC < TTCThreshold)
                    stopTime = Time.time;
                
                if (dist < minVisionRadius)
                {
                    stopTime = Time.time;
                }
            }
        }

        void OnDrawGizmos()
        {
            if (showDebugLine)
            {
                sphereCollider = GetComponent<SphereCollider>();
                if (sphereCollider == null)
                {
                    sphereCollider = gameObject.AddComponent<SphereCollider>();
                    sphereCollider.isTrigger = true;
                }
                sphereCollider.radius = maxVisionRadius;

                Gizmos.color = Color.green;
                if (STOP)
                    Gizmos.color = Color.red;

                var up = Vector3.up * 0.1f;
                Gizmos.DrawLine(transform.position + up + RotateAroundAxis(transform.forward, transform.up, angle) * minVisionRadius, transform.position + up + RotateAroundAxis(transform.forward, transform.up, angle) * maxVisionRadius);
                Gizmos.DrawLine(transform.position + up + RotateAroundAxis(transform.forward, transform.up, -angle) * minVisionRadius, transform.position + up + RotateAroundAxis(transform.forward, transform.up, -angle) * maxVisionRadius);

                float currAngle = -angle;
                for (int i = 0; i < segments; i++)
                {
                    float stepAngle = angle * 2 / segments;

                    Gizmos.DrawLine(transform.position + up + RotateAroundAxis(transform.forward, transform.up, currAngle) * minVisionRadius, transform.position + up + RotateAroundAxis(transform.forward, transform.up, currAngle + stepAngle) * minVisionRadius);
                    Gizmos.DrawLine(transform.position + up + RotateAroundAxis(transform.forward, transform.up, currAngle) * maxVisionRadius, transform.position + up + RotateAroundAxis(transform.forward, transform.up, currAngle + stepAngle) * maxVisionRadius);

                    currAngle += stepAngle;
                }

                Gizmos.color = Color.blue;
                if (capCollider != null)
                    DrawCircle(transform.position + up, capCollider.radius, segments * 2);
            }
        }

        #region Helper Functions

        void DrawCircle(Vector3 center, float radius, int segments)
        {
            float angleStep = 360f / segments;

            Vector3 previousPoint = center + new Vector3(radius, 0, 0);

            for (int i = 1; i <= segments; i++)
            {
                float angle = angleStep * i * Mathf.Deg2Rad;

                Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);

                Gizmos.DrawLine(previousPoint, newPoint);

                previousPoint = newPoint;
            }

            Gizmos.DrawLine(previousPoint, center + new Vector3(radius, 0, 0));
        }

        Vector3 RotateAroundAxis(Vector3 vector, Vector3 axis, float angle)
        {
            axis.Normalize();
            return Quaternion.AngleAxis(angle, axis) * vector;
        }

        // Solves the quadratic |relPos + relVel*t| = (r1 + r2) for the smallest non-negative t.
        // Returns -1 if there is no future collision.
        public float TimeToCollision(Vector2 pos1, Vector2 vel1, float radius1, Vector2 pos2, Vector2 vel2, float radius2)
        {
            Vector2 relativeVelocity = vel1 - vel2;
            Vector2 relativePosition = pos1 - pos2;

            float sumRadii = radius1 + radius2;

            float a = Vector2.Dot(relativeVelocity, relativeVelocity);
            float b = 2f * Vector2.Dot(relativePosition, relativeVelocity);
            float c = Vector2.Dot(relativePosition, relativePosition) - sumRadii * sumRadii;

            float discriminant = b * b - 4 * a * c;

            if (discriminant < 0 || a == 0)
            {
                return -1f;
            }

            float sqrtDiscriminant = Mathf.Sqrt(discriminant);
            float t1 = (-b - sqrtDiscriminant) / (2 * a);
            float t2 = (-b + sqrtDiscriminant) / (2 * a);

            if (t1 >= 0) return t1;
            if (t2 >= 0) return t2;

            return -1f;
        }

        #endregion

        #region Participant Detection

        bool IsParticipant(Collider collider)
        {
            if (collider.gameObject == Camera.main?.gameObject)
            {
                return true;
            }

            if (collider.gameObject.CompareTag("Player"))
            {
                return true;
            }

            if (collider.gameObject.name.Contains("XR") || collider.gameObject.name.Contains("Camera"))
            {
                return true;
            }

            if (Camera.main != null && collider.transform.IsChildOf(Camera.main.transform.root))
            {
                return true;
            }

            return false;
        }
        
        #endregion

        #region Getter Functions
        public Vector3 GetVelocity()
        {
            return velocity;
        }

        #endregion

        #region Cache Management

        /// <summary>Clear the VisionCone lookup cache (call when destroying or pooling agents).</summary>
        public static void ClearCache()
        {
            visionConeCache.Clear();
        }

        /// <summary>Remove a single GameObject from the cache (call when returning an agent to its pool).</summary>
        public static void RemoveFromCache(GameObject obj)
        {
            if (obj != null)
            {
                visionConeCache.Remove(obj.transform.root.gameObject);
            }
        }
        #endregion
    }
}

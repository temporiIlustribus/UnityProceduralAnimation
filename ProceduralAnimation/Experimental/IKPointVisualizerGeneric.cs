using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralAnimation
{
    public class GenericIKPointVisualizer<FootSolver, SP> : MonoBehaviour 
        where FootSolver : MonoBehaviour, IIKFootSolver<SP>
        where SP : new()
    {
        [SerializeField] FootSolver footIK;
        [SerializeField] bool visulizeKeyPoints;
        [SerializeField] float markerSize = 0.1f;
        [SerializeField] bool showGrid = true;
        [SerializeField] Vector2 gridDimensions = new Vector2(2.0f, 2.0f);
        [SerializeField] [Range(1e-3f, 0.25f)] float gridSize = 0.1f;
        List<RaycastHit> points = new List<RaycastHit>();

        private void CalculateGrid() {
            float xLimit = -gridDimensions.x / 2 + footIK.Body.position.x;
            points = new List<RaycastHit>();
            while (xLimit < gridDimensions.x / 2 + footIK.Body.position.x)
            {
                float zLimit = -gridDimensions.y / 2 + footIK.Body.position.z;
                while (zLimit < gridDimensions.y / 2 + footIK.Body.position.z)
                {
                    Vector3 point = new Vector3(Mathf.Floor(xLimit / gridSize) * gridSize, footIK.Body.position.y, Mathf.Floor(zLimit / gridSize) * gridSize);
                    if (Physics.Raycast(point, Vector3.down, out RaycastHit pointInfo, float.PositiveInfinity, footIK.TerrainLayer))
                        points.Add(pointInfo);
                    zLimit += gridSize;
                }
                xLimit += gridSize;
            }
            points.Sort((lhs, rhs) => { return footIK.ComparePositions(footIK.DesiredPos, lhs, rhs); });
        }
        Vector3 bodyPos;
        private void OnDrawGizmos()
        {
            if (visulizeKeyPoints)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(footIK.EffectorPosition, 0.1f);
                Gizmos.DrawRay(footIK.EffectorPosition, footIK.EffectorNormal);

                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(footIK.TargetPosition, 0.1f);
                Gizmos.DrawRay(footIK.TargetPosition, footIK.TargetNormal);

                Gizmos.color = Color.red;
                Gizmos.DrawSphere(footIK.BarrierTopPos, 0.1f);
                Gizmos.DrawCube(footIK.BarrierHit, new Vector3(0.1f, 0.1f, 0.1f));

                Gizmos.color = Color.green;
                Gizmos.DrawSphere(footIK.DesiredPos, 0.1f);

                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(footIK.InitialPos, Vector3.up);
                if (footIK.LocalSearchPoints != null)
                {
                    Gizmos.color = Color.white;
                    for (int i = 0; i < footIK.LocalSearchPoints.Count; ++i)
                    {
                        Gizmos.DrawCube(footIK.LocalSearchPoints[i].point, new Vector3(0.07f, 0.07f, 0.07f));
                        Gizmos.DrawRay(footIK.LocalSearchPoints[i].point, footIK.LocalSearchPoints[i].normal);
                    }
                }
            }
            if (showGrid)
            {
                if (Vector3.Distance(bodyPos, footIK.Body.position) > 0.1f || footIK.isMoving())
                {
                    bodyPos = footIK.Body.position;
                    CalculateGrid();
                }
                for (int i = 0; i < points.Count; i++)
                {
                    Gizmos.color = Color.Lerp(Color.green, Color.red, ((float)i) / points.Count);
                    Gizmos.DrawSphere(points[i].point, markerSize);
                }
                Gizmos.color = Color.blue;
                Gizmos.DrawCube(footIK.TargetPosition, new Vector3(markerSize, markerSize, markerSize));
                Gizmos.DrawRay(footIK.TargetPosition, footIK.TargetNormal);
            }
        }
    }
}
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Shared grid-based path follower that:
    /// - Uses A* on GridWorld
    /// - Walks along cell centers
    /// - Rotates the Unit toward the next cell center
    /// - Exposes local-space movement intent for CharacterMotor
    /// </summary>
    public class GridPathFollower
    {
        private const bool DebugEnabled = true;
        private const float DebugLogIntervalSeconds = 1f;

        private readonly Unit unit;
        private readonly GridWorld gridWorld;
        private readonly AStarNavigator navigator;
        private readonly float turnSpeed;
        private readonly float arrivalThreshold;

        private List<Vector2Int> currentPath;
        private int currentPathIndex;
        private bool isPathPending;
        private Vector2Int? destinationCell;

        private float nextDebugLogTime;

        public Vector2 CurrentMoveInput { get; private set; }

        public bool HasActivePath => currentPath != null && currentPathIndex < currentPath.Count;

        public GridPathFollower(Unit unit, GridWorld gridWorld, float turnSpeed = 18f, float arrivalThreshold = 0.1f)
        {
            this.unit = unit;
            this.gridWorld = gridWorld;
            this.turnSpeed = turnSpeed;
            this.arrivalThreshold = arrivalThreshold;

            navigator = new AStarNavigator(gridWorld);
        }

        public void SetDestination(Vector2Int targetCell)
        {
            if (destinationCell.HasValue && destinationCell.Value == targetCell)
            {
                return;
            }

            destinationCell = targetCell;
            currentPath = null;
            currentPathIndex = 0;
            isPathPending = false;
        }

        public TaskStatus Update()
        {
            CurrentMoveInput = Vector2.zero;

            if (unit == null || gridWorld == null || !destinationCell.HasValue)
            {
                return TaskStatus.Failure;
            }

            Vector2Int targetGridPos = destinationCell.Value;
            Vector2Int startGridPos = gridWorld.WorldToGrid(unit.transform.position);

            // If we're already in the target grid cell, we're done.
            if (startGridPos == targetGridPos)
            {
                currentPath = null;
                currentPathIndex = 0;
                return TaskStatus.Success;
            }

            // Request a path if we don't have one and aren't already calculating
            if ((currentPath == null || currentPathIndex >= currentPath.Count) && !isPathPending)
            {
                RequestPathAsync(startGridPos, targetGridPos);
                return TaskStatus.Running;
            }

            // Still waiting on path calculation
            if (isPathPending)
            {
                return TaskStatus.Running;
            }

            // If path calculation failed
            if (currentPath == null)
            {
                if (DebugEnabled)
                {
                    Debug.LogWarning($"[GridPathFollower] Path is null after calculation. start={startGridPos} goal={targetGridPos}");
                }
                return TaskStatus.Failure;
            }

            // If the path is empty but not null, treat as already at target.
            if (currentPath.Count == 0)
            {
                if (DebugEnabled)
                {
                    Debug.Log($"[GridPathFollower] Empty path (start==goal). Treating as arrived at target={targetGridPos}.");
                }
                currentPath = null;
                currentPathIndex = 0;
                return TaskStatus.Success;
            }

            // Move along the path toward the center of the current waypoint cell
            Vector2Int currentWaypointGrid = currentPath[currentPathIndex];
            Vector3 currentWaypointWorld = gridWorld.GridToWorld(currentWaypointGrid);

            Vector3 unitPos = unit.transform.position;
            Vector3 direction = currentWaypointWorld - unitPos;
            direction.y = 0f;

            float distanceToWaypoint = direction.magnitude;

            if (distanceToWaypoint > 0.0001f)
            {
                Vector3 desiredForward = direction / distanceToWaypoint;

                // Smoothly rotate the unit to face the next cell center
                Quaternion targetRotation = Quaternion.LookRotation(desiredForward, Vector3.up);
                unit.transform.rotation = Quaternion.Slerp(
                    unit.transform.rotation,
                    targetRotation,
                    turnSpeed * Time.deltaTime
                );

                // Always move forward in local space while in a moving state (full speed)
                CurrentMoveInput = Vector2.up;
            }

            if (DebugEnabled && Time.time >= nextDebugLogTime)
            {
                nextDebugLogTime = Time.time + DebugLogIntervalSeconds;
                Debug.Log(
                    $"[GridPathFollower] waypoint={currentPathIndex + 1}/{currentPath.Count} " +
                    $"waypointGrid={currentWaypointGrid} dist={distanceToWaypoint:0.00} " +
                    $"pos={unit.transform.position} targetWorld={currentWaypointWorld}");
            }

            // Check if we've reached the current waypoint (cell center)
            if (distanceToWaypoint < arrivalThreshold)
            {
                currentPathIndex++;

                // If we've reached the end of the path, we're at the destination
                if (currentPathIndex >= currentPath.Count)
                {
                    currentPath = null;
                    currentPathIndex = 0;
                    return TaskStatus.Success;
                }
            }

            return TaskStatus.Running;
        }

        private async void RequestPathAsync(Vector2Int startGridPos, Vector2Int targetGridPos)
        {
            if (isPathPending)
            {
                return;
            }

            isPathPending = true;

            if (DebugEnabled)
            {
                GridCell startCell = gridWorld.GetCell(startGridPos);
                GridCell goalCell = gridWorld.GetCell(targetGridPos);
                bool startNav = gridWorld.IsPositionNavigable(startGridPos);
                bool goalNav = gridWorld.IsPositionNavigable(targetGridPos);

                Debug.Log(
    $"[GridPathFollower] RequestPath start={startGridPos} goal={targetGridPos} " +
    $"startCell={(startCell != null ? "OK" : "NULL")} goalCell={(goalCell != null ? "OK" : "NULL")} " +
    $"startNavigable={startNav} goalNavigable={goalNav}");
            }

            List<Vector2Int> path = await navigator.FindPathAsync(startGridPos, targetGridPos);

            currentPath = path;
            currentPathIndex = 0;
            isPathPending = false;

            if (DebugEnabled)
            {
                if (currentPath == null)
                {
                    Debug.LogWarning($"[GridPathFollower] Path result: NULL (no path) start={startGridPos} goal={targetGridPos}");
                }
                else
                {
                    string endpoints = currentPath.Count > 0
                        ? $"first={currentPath[0]} last={currentPath[currentPath.Count - 1]}"
                        : "empty";
                    Debug.Log($"[GridPathFollower] Path result: count={currentPath.Count} {endpoints}");
                }
            }
        }
    }
}


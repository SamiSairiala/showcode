
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObstacleAvoidanceBehaviour : SteeringBehaviour
{
    [SerializeField] private float radius = 2f, enemyColliderSize = 0.6f; // Avoid if in 2f, Avoid at any cost if in 0.6f.
    [SerializeField] private bool ShowGizmos = true;
    float[] dangersResultTemp = null; // gizmo params.


    public override (float[] danger, float[] intrest) GetSteering(float[] danger, float[] intrest, AIData aiData)
    {
        foreach (Collider2D obstacleCollider in aiData.obstacles)
        {
            if (obstacleCollider == null) continue;
            Vector2 directionToObstacle = obstacleCollider.ClosestPoint(transform.position) - (Vector2)transform.position;
            float distanceToObstacle = directionToObstacle.magnitude;
            float weight = distanceToObstacle <= enemyColliderSize ? 1 : (radius - distanceToObstacle) / radius; // Calculate the weight on how far is the enemy and obstacle
            Vector2 directionToObstacleNormalized = directionToObstacle.normalized;

            for (int i = 0; i < Directions.eightDirections.Count; i++) // Add params to the danger array (what to avoid)
            {
                float result = Vector2.Dot(directionToObstacleNormalized, Directions.eightDirections[i]);
                float valuetoputin = result * weight;

                if (valuetoputin > danger[i]) //Override the value if it's higher than current danger value
                {
                    danger[i] = valuetoputin;
                }
            }
        }
        dangersResultTemp = danger;
        return (danger, intrest);
    }
    private void OnDrawGizmos()
    {
        if (ShowGizmos == false)
            return;

        if (Application.isPlaying && dangersResultTemp != null)
        {
            if (dangersResultTemp != null)
            {
                Gizmos.color = Color.red;
                for (int i = 0; i < dangersResultTemp.Length; i++)
                {
                    Gizmos.DrawRay(
                        transform.position,
                        Directions.eightDirections[i] * dangersResultTemp[i] * 2
                        );
                }
            }
        }




    }
}


public static class Directions
{
    public static List<Vector2> eightDirections = new List<Vector2>
    {
        new Vector2(0,1).normalized,
        new Vector2(1,1).normalized,
        new Vector2(1,0).normalized,
        new Vector2(1,-1).normalized,
        new Vector2(0,-1).normalized,
        new Vector2(-1,-1).normalized,
        new Vector2(-1,0).normalized,
        new Vector2(-1,1).normalized
    };
}

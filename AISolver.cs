using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AISolver : MonoBehaviour
{
    [SerializeField] private bool ShowGizmos = true;
    float[] intrestGizmo = new float[0];
    Vector2 resultDirection = Vector2.zero;
    private float rayLenght = 2;

    private void Start()
    {
        intrestGizmo = new float[8];
    }

    public Vector2 GetDirectionToMove(List<SteeringBehaviour> behaviours, AIData aiData)
    {
        float[] danger = new float[8];
        float[] intrest = new float[8];

        //Loop through all of the behaviours.
        foreach(SteeringBehaviour behaviour in behaviours)
        {
            (danger, intrest) = behaviour.GetSteering(danger, intrest, aiData); 
        }

        //Minus danger values from intrest values
        for(int i = 0; i < 8; i++)
        {
            intrest[i] = Mathf.Clamp01(intrest[i] - danger[i]);
        }

        intrestGizmo = intrest;

        //average direction
        //also smooths the direction
        Vector2 outputDirection = Vector2.zero;
        for(int i = 0; i < 8; i++)
        {
            outputDirection += Directions.eightDirections[i] * intrest[i];
        }
        outputDirection.Normalize();
        resultDirection = outputDirection;

        //return movement direction
        return resultDirection;
    }
    private void OnDrawGizmos()
    {
        if (Application.isPlaying && ShowGizmos)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, resultDirection * rayLenght);
        }
    }
}

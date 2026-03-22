using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RaycASsCRIPT : MonoBehaviour
{
        public GameObject centereye;

        public float maxdistance=2;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {   
           RaycastHit hit;
        // Does the ray intersect any objects excluding the player layer
        if (Physics.Raycast(centereye.transform.position, centereye.transform.TransformDirection(Vector3.forward), out hit, maxdistance, 0))
        {
               // hit.collider.gameObject.GetComponent<checklistpointer>().Addtochecklist();

           // Debug.DrawRay(centereye.transform.position, centereye.transform.TransformDirection(Vector3.forward) * hit.distance, Color.yellow);
           Debug.Log("Did Hit");
        }
        else
        {
            Debug.DrawRay(centereye.transform.position, centereye.transform.TransformDirection(Vector3.forward) * maxdistance, Color.white);
            //Debug.Log("Did not Hit");
        }
    }
}

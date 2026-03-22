using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class valvescript : MonoBehaviour
{
    public int tipovalve=0;
    public float valuepress=0;
    public GameObject rotador;

    public float valuef = .01f;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
           valuepress += 1;

        //*Time.deltaTime*valuef
      // rotador.transform.localRotation *= Quaternion.Euler(0,1*valuef,0);

    }
}

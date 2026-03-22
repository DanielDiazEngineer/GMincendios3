using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class spinscript1 : MonoBehaviour
{

    public int modo=1;
    public float speed=1;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
       // this.gameObject.transform.RotateEuler(0,1,0);
        transform.RotateAround(transform.position, Vector3.up, speed * Time.deltaTime);

    }
}

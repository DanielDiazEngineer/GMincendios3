using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class lightstrobescript : MonoBehaviour
{

    //Lights
    
    // Start is called before the first frame update
    void Start()
    {
        //lights =GetComponent<Light>();
    }

    // Update is called once per frame
    void Update()
    {
    GetComponent<Light>().intensity = Mathf.PingPong(122000* Time.deltaTime /32 ,11)+2;
    }
}

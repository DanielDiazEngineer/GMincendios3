using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class leveldemo : MonoBehaviour
{

    public GameObject canvasconato;
    public GameObject fire1;
    public GameObject fire2;
    public GameObject extin;
    private AudioSource audio;
    // Start is called before the first frame update
    void Start()
    {
        audio = GetComponent<AudioSource>();
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void AboutStart()
    {
        audio.Play();

        canvasconato.SetActive(true);
        fire1.SetActive(true);
        fire2.SetActive(true);
        extin.SetActive(true);
    }
}

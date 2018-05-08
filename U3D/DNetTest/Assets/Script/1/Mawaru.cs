using UnityEngine;
using System.Collections;

/// <summary>
/// 自动旋转
/// </summary>
public class Mawaru : MonoBehaviour
{

	// Use this for initialization
	void Start () {
	
	}

    public void FixedUpdate()
    {
        transform.Rotate(Vector3.up, Time.deltaTime * 360 * 0.2f);
    }
	

	// Update is called once per frame
 
}
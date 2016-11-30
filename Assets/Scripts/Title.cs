using UnityEngine;
using System.Collections;

public class Title : MonoBehaviour {
    public Canvas titleCanvas;
    public Canvas joinCanvas;

    public void titleButton() {
        titleCanvas.gameObject.SetActive(false);
        joinCanvas.gameObject.SetActive(true);
    }
}

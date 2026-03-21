using UnityEngine;

public class HideOnStart : MonoBehaviour
{
    private void Start()
    {
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }
        else
        {
            Debug.LogWarning($"HideOnStart: No MeshRenderer found on {gameObject.name}");
        }
    }
}
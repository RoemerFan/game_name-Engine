 
 
 
 
 
using System.Collections.Generic;
using UnityEngine;
 
public class CoalMiningPoint : MonoBehaviour
{
    [Header("Kohlestuecke")]
    [SerializeField] private GameObject coalChunkPhysicsPrefab;
    [SerializeField] private Transform[] embeddedPositions;
 
    [Header("Abbau")]
    [SerializeField] private float secondsPerChunk = 2f;
 
    [Header("Auswurf")]
    [SerializeField] private float upwardForce = 2f;
    [SerializeField] private float sidewaysForce = 1.5f;
 
    private readonly List<GameObject> embeddedChunks =
        new List<GameObject>();
 
    private float miningProgress;
    private Collider miningCollider;
 
    private int RemainingChunks => embeddedChunks.Count;
 
    private void Awake()
    {
        miningCollider = GetComponent<Collider>();
    }
 
    private void Start()
    {
        embeddedChunks.Clear();
 
        foreach (Transform slot in embeddedPositions)
        {
            if (slot == null || slot.childCount == 0)
            {
                Debug.LogWarning(
                    "Ein Mining-Slot enthaelt kein sichtbares Kohlestueck!",
                    this
                );
                continue;
            }
 
            // Das sichtbare Kohlestueck liegt als erstes Kind im Slot.
            GameObject visualChunk = slot.GetChild(0).gameObject;
            embeddedChunks.Add(visualChunk);
        }
 
        Debug.Log(
            "Kohlestuecke an dieser Abbaustelle: " + RemainingChunks,
            this
        );
    }
 
    public void Mine(float deltaTime)
    {
        if (RemainingChunks <= 0)
            return;
 
        miningProgress += deltaTime;
 
        if (miningProgress >= secondsPerChunk)
        {
            miningProgress -= secondsPerChunk;
            ReleaseCoal();
        }
    }
 
    private void ReleaseCoal()
    {
        if (embeddedChunks.Count == 0)
            return;
 
        int lastIndex = embeddedChunks.Count - 1;
        GameObject visualChunk = embeddedChunks[lastIndex];
        embeddedChunks.RemoveAt(lastIndex);
 
        // Position und Drehung vor dem Entfernen merken.
        Vector3 spawnPosition = visualChunk.transform.position;
        Quaternion spawnRotation = visualChunk.transform.rotation;
 
        // Nur das sichtbare Stück im Felsen entfernen.
        Destroy(visualChunk);
 
        // Ein neues, physikalisches Kohlestück erzeugen.
        if (coalChunkPhysicsPrefab != null)
        {
            GameObject physicsChunk = Instantiate(
                coalChunkPhysicsPrefab,
                spawnPosition,
                spawnRotation
            );
 
            Rigidbody rb = physicsChunk.GetComponent<Rigidbody>();
 
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
 
                Vector3 direction = new Vector3(
                    Random.Range(-1f, 1f),
                    1f,
                    Random.Range(-1f, 1f)
                ).normalized;
 
                rb.AddForce(
                    direction * sidewaysForce
                    + Vector3.up * upwardForce,
                    ForceMode.Impulse
                );
            }
            else
            {
                Debug.LogWarning(
                    "Dem Physics-Prefab fehlt ein Rigidbody!",
                    physicsChunk
                );
            }
        }
        else
        {
            Debug.LogError(
                "Coal Chunk Physics Prefab wurde nicht zugewiesen!",
                this
            );
        }
 
        Debug.Log("Noch vorhandene Kohlestuecke: " + RemainingChunks);
 
        if (RemainingChunks <= 0)
        {
            if (miningCollider != null)
                miningCollider.enabled = false;
 
            Debug.Log("Diese Abbaustelle ist erschoepft!");
        }
    }
}
 
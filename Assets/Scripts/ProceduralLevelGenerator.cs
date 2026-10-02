using System.Collections.Generic;
using UnityEngine;

public class ProceduralLevelGenerator : MonoBehaviour
{
    [Header("Geração")]
    [SerializeField] private int chunkSize = 12;
    [SerializeField] private int viewDistance = 2;
    [SerializeField] private float blockSize = 2f;
    [SerializeField] private float wallHeight = 3.2f;

    [Header("Player Prefab")]
    [SerializeField] private GameObject playerPrefab;

    [Header("Estilo cartoon")]
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material ceilingMaterial;

    [Header("Movimento")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float runSpeed = 10f;

    private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    private Vector2Int currentChunk = new Vector2Int(int.MinValue, int.MinValue);

    private GameObject player;
    private Rigidbody rb;
    private Vector3 moveInput;

    private void Awake()
    {
        if (floorMaterial == null || wallMaterial == null || ceilingMaterial == null)
        {
            CreateCartoonMaterials();
        }

        SpawnPlayer();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Start()
    {
        RefreshChunksAroundWorldPosition(Vector3.zero);
    }

    private void Update()
    {
        HandlePlayerInput();
        UpdateChunkGeneration();
    }

    private void FixedUpdate()
    {
        ApplyMovement();
    }

    private void SpawnPlayer()
    {
        if (playerPrefab != null)
        {
            player = Instantiate(playerPrefab, new Vector3(0f, 2f, 0f), Quaternion.identity);
            player.name = "Player";
        }
        else
        {
            player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 2f, 0f);
            player.transform.localScale = new Vector3(0.8f, 1f, 0.8f);

            Renderer rend = player.GetComponent<Renderer>();
            if (rend != null)
                rend.enabled = false;
        }

        rb = player.GetComponent<Rigidbody>();
    }

    private void HandlePlayerInput()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 input = new Vector3(horizontal, 0f, vertical);
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;

        if (Camera.main != null)
        {
            forward = Vector3.Scale(Camera.main.transform.forward, new Vector3(1f, 0f, 1f)).normalized;
            right = Vector3.Scale(Camera.main.transform.right, new Vector3(1f, 0f, 1f)).normalized;
        }

        moveInput = (forward * input.z + right * input.x).normalized;
    }

    private void ApplyMovement()
    {
        if (rb == null || player == null)
            return;

        float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : moveSpeed;

        Vector3 moveForce = moveInput * speed;
        moveForce.y = rb.velocity.y;

        rb.velocity = moveForce;

        if (moveInput.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveInput, Vector3.up);
            player.transform.rotation = Quaternion.Slerp(player.transform.rotation, targetRot, 12f * Time.deltaTime);
        }
    }

    private void UpdateChunkGeneration()
    {
        Vector3 playerPos = player.transform.position;
        Vector2Int chunkCoord = new Vector2Int(
            Mathf.FloorToInt(playerPos.x / (chunkSize * blockSize)),
            Mathf.FloorToInt(playerPos.z / (chunkSize * blockSize))
        );

        if (chunkCoord == currentChunk)
            return;

        currentChunk = chunkCoord;
        RefreshChunksAroundWorldPosition(playerPos);
    }

    private void RefreshChunksAroundWorldPosition(Vector3 worldPos)
    {
        Vector2Int originChunk = new Vector2Int(
            Mathf.FloorToInt(worldPos.x / (chunkSize * blockSize)),
            Mathf.FloorToInt(worldPos.z / (chunkSize * blockSize))
        );

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                Vector2Int coord = new Vector2Int(originChunk.x + x, originChunk.y + z);

                if (!chunks.ContainsKey(coord))
                {
                    Chunk chunk = CreateChunk(coord);
                    chunks.Add(coord, chunk);
                }
            }
        }

        List<Vector2Int> remove = new List<Vector2Int>();
        foreach (var kvp in chunks)
        {
            Vector2Int c = kvp.Key;
            int dx = Mathf.Abs(c.x - originChunk.x);
            int dz = Mathf.Abs(c.y - originChunk.y);

            if (dx > viewDistance || dz > viewDistance)
            {
                remove.Add(c);
            }
        }

        foreach (var k in remove)
        {
            if (chunks.TryGetValue(k, out var chunk))
            {
                if (chunk.Root != null)
                    Destroy(chunk.Root);

                chunks.Remove(k);
            }
        }
    }

    private Chunk CreateChunk(Vector2Int coord)
    {
        GameObject root = new GameObject($"Chunk_{coord.x}_{coord.y}");
        root.transform.SetParent(transform);

        MeshFilter mf = root.AddComponent<MeshFilter>();
        MeshRenderer mr = root.AddComponent<MeshRenderer>();
        MeshCollider mc = root.AddComponent<MeshCollider>();
        mc.convex = false;

        mr.materials = new[] { floorMaterial, wallMaterial, ceilingMaterial };

        Chunk chunk = new Chunk
        {
            Coord = coord,
            Root = root,
            MeshFilter = mf,
            MeshRenderer = mr,
            MeshCollider = mc
        };

        BuildChunkMesh(chunk);
        return chunk;
    }

    private void BuildChunkMesh(Chunk chunk)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>[] { new List<int>(), new List<int>(), new List<int>() };

        float minX = chunk.Coord.x * chunkSize * blockSize;
        float minZ = chunk.Coord.y * chunkSize * blockSize;

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                float px = minX + x * blockSize;
                float pz = minZ + z * blockSize;

                Vector3 a = new Vector3(px, 0f, pz);
                Vector3 b = new Vector3(px + blockSize, 0f, pz);
                Vector3 c = new Vector3(px + blockSize, 0f, pz + blockSize);
                Vector3 d = new Vector3(px, 0f, pz + blockSize);

                AddQuad(vertices, triangles, a, b, c, d, 0);

                Vector3 e = a + Vector3.up * wallHeight;
                Vector3 f = b + Vector3.up * wallHeight;
                Vector3 g = c + Vector3.up * wallHeight;
                Vector3 h = d + Vector3.up * wallHeight;

                AddQuad(vertices, triangles, e, h, g, f, 2);
            }
        }

        for (int x = 0; x < chunkSize; x++)
        {
            for (int z = 0; z < chunkSize; z++)
            {
                bool edge = x == 0 || z == 0 || x == chunkSize - 1 || z == chunkSize - 1;

                float px = minX + x * blockSize;
                float pz = minZ + z * blockSize;

                float sample = Mathf.PerlinNoise(
                    ((chunk.Coord.x * chunkSize) + x + 11) * 0.26f,
                    ((chunk.Coord.y * chunkSize) + z + 17) * 0.26f
                );

                if (edge || sample > 0.68f)
                {
                    Vector3 center = new Vector3(px + blockSize * 0.5f, wallHeight * 0.5f, pz + blockSize * 0.5f);
                    AddBox(vertices, triangles, center, new Vector3(blockSize, wallHeight, blockSize), 1);
                }
            }
        }

        Mesh mesh = chunk.MeshFilter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh();
            chunk.MeshFilter.sharedMesh = mesh;
        }

        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.subMeshCount = 3;

        mesh.SetTriangles(triangles[0], 0);
        mesh.SetTriangles(triangles[1], 1);
        mesh.SetTriangles(triangles[2], 2);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        chunk.MeshCollider.sharedMesh = null;
        chunk.MeshCollider.sharedMesh = mesh;
    }

    private static void AddQuad(List<Vector3> vertices, List<int>[] triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int subMeshIndex)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        triangles[subMeshIndex].Add(start + 0);
        triangles[subMeshIndex].Add(start + 1);
        triangles[subMeshIndex].Add(start + 2);

        triangles[subMeshIndex].Add(start + 0);
        triangles[subMeshIndex].Add(start + 2);
        triangles[subMeshIndex].Add(start + 3);
    }

    private static void AddBox(List<Vector3> vertices, List<int>[] triangles, Vector3 center, Vector3 size, int subMeshIndex)
    {
        Vector3 half = size * 0.5f;

        Vector3 p000 = center + new Vector3(-half.x, -half.y, -half.z);
        Vector3 p100 = center + new Vector3(half.x, -half.y, -half.z);
        Vector3 p110 = center + new Vector3(half.x, -half.y, half.z);
        Vector3 p010 = center + new Vector3(-half.x, -half.y, half.z);
        Vector3 p001 = center + new Vector3(-half.x, half.y, -half.z);
        Vector3 p101 = center + new Vector3(half.x, half.y, -half.z);
        Vector3 p111 = center + new Vector3(half.x, half.y, half.z);
        Vector3 p011 = center + new Vector3(-half.x, half.y, half.z);

        AddQuad(vertices, triangles, p000, p100, p110, p010, subMeshIndex);
        AddQuad(vertices, triangles, p001, p011, p111, p101, subMeshIndex);
        AddQuad(vertices, triangles, p000, p001, p101, p100, subMeshIndex);
        AddQuad(vertices, triangles, p010, p110, p111, p011, subMeshIndex);
        AddQuad(vertices, triangles, p000, p010, p011, p001, subMeshIndex);
        AddQuad(vertices, triangles, p100, p101, p111, p110, subMeshIndex);
    }

    private void CreateCartoonMaterials()
    {
        floorMaterial = new Material(Shader.Find("Standard"));
        floorMaterial.color = new Color(0.78f, 0.73f, 0.56f);
        floorMaterial.SetFloat("_Glossiness", 0f);
        floorMaterial.SetFloat("_Metallic", 0f);

        wallMaterial = new Material(Shader.Find("Standard"));
        wallMaterial.color = new Color(0.93f, 0.90f, 0.80f);
        wallMaterial.SetFloat("_Glossiness", 0f);
        wallMaterial.SetFloat("_Metallic", 0f);

        ceilingMaterial = new Material(Shader.Find("Standard"));
        ceilingMaterial.color = new Color(0.97f, 0.95f, 0.88f);
        ceilingMaterial.SetFloat("_Glossiness", 0f);
        ceilingMaterial.SetFloat("_Metallic", 0f);
    }

    private class Chunk
    {
        public Vector2Int Coord;
        public GameObject Root;
        public MeshFilter MeshFilter;
        public MeshRenderer MeshRenderer;
        public MeshCollider MeshCollider;
    }
}

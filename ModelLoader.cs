using Assimp;
using Assimp.Configs;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.IO;
using System.Linq;
using Buffer = SharpDX.Direct3D11.Buffer;
using Color = SharpDX.Color;
using Device = SharpDX.Direct3D11.Device;

namespace Undistort
{
    internal class ModelLoader
    {
        private Device m_device;
        private AssimpContext m_importer;
        private String m_modelPath;

        public ModelLoader(Device device)
        {
            m_device = device;
            m_importer = new AssimpContext();
            m_importer.SetConfig(new NormalSmoothingAngleConfig(66.0f));
        }

        //Load model
        public Model Load(String fileName)
        {
            if (!File.Exists(fileName))
                throw new Exception("File " + fileName + " doesn't exist.");
            Scene scene = m_importer.ImportFile(fileName, PostProcessPreset.TargetRealTimeMaximumQuality);

            //use this directory path to load textures from
            m_modelPath = Path.GetDirectoryName(fileName);

            Model model = new Model();
            Matrix identity = Matrix.Identity;

            AddVertexData(model, scene, scene.RootNode, m_device, ref identity);
            ComputeBoundingBox(model, scene);

            return model;
        }

        //calculates the bounding box of the whole model
        private void ComputeBoundingBox(Model model, Scene scene)
        {
            Vector3 sceneMin = new Vector3(1e10f, 1e10f, 1e10f);
            Vector3 sceneMax = new Vector3(-1e10f, -1e10f, -1e10f);
            Matrix transform = Matrix.Identity;

            ComputeBoundingBox(scene, scene.RootNode, ref sceneMin, ref sceneMax, ref transform);

            //set min and max of bounding box
            model.SetAABox(sceneMin, sceneMax);
        }

        //recursively calculates the bounding box of the whole model
        private void ComputeBoundingBox(Scene scene, Node node, ref Vector3 min, ref Vector3 max, ref Matrix transform)
        {
            Matrix previousTransform = transform;
            transform = Matrix.Multiply(previousTransform, FromMatrix(node.Transform));

            if (node.HasMeshes)
            {
                foreach (int index in node.MeshIndices)
                {
                    Assimp.Mesh mesh = scene.Meshes[index];
                    for (int i = 0; i < mesh.VertexCount; i++)
                    {
                        Vector3 tmp = FromVector(mesh.Vertices[i]);
                        Vector4 result;
                        Vector3.Transform(ref tmp, ref transform, out result);

                        min.X = Math.Min(min.X, result.X);
                        min.Y = Math.Min(min.Y, result.Y);
                        min.Z = Math.Min(min.Z, result.Z);

                        max.X = Math.Max(max.X, result.X);
                        max.Y = Math.Max(max.Y, result.Y);
                        max.Z = Math.Max(max.Z, result.Z);
                    }
                }
            }

            //go down the hierarchy if children are present
            for (int i = 0; i < node.ChildCount; i++)
            {
                ComputeBoundingBox(scene, node.Children[i], ref min, ref max, ref transform);
            }
            transform = previousTransform;
        }

        //determine the number of elements in the vertex
        private int GetNoofInputElements(Assimp.Mesh mesh)
        {

            bool hasTexCoords = mesh.HasTextureCoords(0);
            bool hasColors = mesh.HasVertexColors(0);
            bool hasNormals = mesh.HasNormals;
            bool hasTangents = mesh.Tangents != null && mesh.Tangents.Count > 0;
            bool hasBitangents = mesh.BiTangents != null && mesh.BiTangents.Count > 0;

            int noofElements = 1;

            if (hasColors)
                noofElements++;

            if (hasNormals)
                noofElements++;

            if (hasTangents)
                noofElements++;

            if (hasBitangents)
                noofElements++;

            if (hasTexCoords)
                noofElements++;

            return noofElements;
        }

        //Create meshes and add vertex and index buffers
        private void AddVertexData(Model model, Scene scene, Node node, Device device, ref Matrix transform)
        {
            Matrix previousTransform = transform;
            transform = Matrix.Multiply(previousTransform, FromMatrix(node.Transform));

            //also calculate inverse transpose matrix for normal/tangent/bitagent transformation
            Matrix invTranspose = transform;
            invTranspose.Invert();
            invTranspose.Transpose();

            if (node.HasMeshes)
            {
                foreach (int index in node.MeshIndices)
                {
                    //get a mesh from the scene
                    Assimp.Mesh mesh = scene.Meshes[index];

                    //create new mesh to add to model
                    ModelMesh modelMesh = new ModelMesh();
                    model.AddMesh(ref modelMesh);

                    //if mesh has a material extract the diffuse texture, if present
                    Material material = scene.Materials[mesh.MaterialIndex];
                    if (material != null && material.GetMaterialTextureCount(TextureType.Diffuse) > 0)
                    {
                        material.GetMaterialTexture(TextureType.Diffuse, 0, out TextureSlot texture);
                        //create new texture for mesh
                        modelMesh.AddTextureDiffuse(device, m_modelPath + "\\" + texture.FilePath);
                    }

                    //determine the elements in the vertex
                    bool hasTexCoords = mesh.HasTextureCoords(0);
                    bool hasColors = mesh.HasVertexColors(0);
                    bool hasNormals = mesh.HasNormals;
                    bool hasTangents = mesh.Tangents != null && mesh.Tangents.Count > 0;
                    bool hasBitangents = mesh.BiTangents != null && mesh.BiTangents.Count > 0;

                    //create vertex element list 
                    InputElement[] vertexElements = new InputElement[GetNoofInputElements(mesh)];
                    uint elementIndex = 0;
                    vertexElements[elementIndex++] = new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0);
                    short vertexSize = (short)Utilities.SizeOf<Vector3>();

                    if (hasColors)
                    {
                        vertexElements[elementIndex++] = new InputElement("COLOR", 0, Format.R8G8B8A8_UInt, vertexSize, 0);
                        vertexSize += (short)Utilities.SizeOf<Color>();
                    }
                    if (hasNormals)
                    {
                        vertexElements[elementIndex++] = new InputElement("NORMAL", 0, Format.R32G32B32_Float, vertexSize, 0);
                        vertexSize += (short)Utilities.SizeOf<Vector3>();
                    }
                    if (hasTangents)
                    {
                        vertexElements[elementIndex++] = new InputElement("TANGENT", 0, Format.R32G32B32_Float, vertexSize, 0);
                        vertexSize += (short)Utilities.SizeOf<Vector3>();
                    }
                    if (hasBitangents)
                    {
                        vertexElements[elementIndex++] = new InputElement("BITANGENT", 0, Format.R32G32B32_Float, vertexSize, 0);
                        vertexSize += (short)Utilities.SizeOf<Vector3>();
                    }
                    if (hasTexCoords)
                    {
                        vertexElements[elementIndex++] = new InputElement("TEXCOORD", 0, Format.R32G32_Float, vertexSize, 0);
                        vertexSize += (short)Utilities.SizeOf<Vector2>();
                    }

                    //set the vertex elements and size
                    modelMesh.InputElements = vertexElements;
                    modelMesh.VertexSize = vertexSize;

                    //get pointers to vertex data
                    var positions = mesh.Vertices;
                    var texCoords = mesh.TextureCoordinateChannels[0];
                    var normals = mesh.Normals;
                    var tangents = mesh.Tangents;
                    var biTangents = mesh.BiTangents;
                    var colours = mesh.VertexColorChannels[0];

                    //also determine primitive type
                    switch (mesh.PrimitiveType)
                    {
                        case PrimitiveType.Point:
                            modelMesh.PrimitiveTopology = PrimitiveTopology.PointList;
                            break;
                        case PrimitiveType.Line:
                            modelMesh.PrimitiveTopology = PrimitiveTopology.LineList;
                            break;
                        case PrimitiveType.Triangle:
                            modelMesh.PrimitiveTopology = PrimitiveTopology.TriangleList;
                            break;
                        default:
                            throw new Exception("ModelLoader::AddVertexData(): Unknown primitive type");
                    }

                    //create data stream for vertices
                    DataStream vertexStream = new DataStream(mesh.VertexCount * vertexSize, true, true);

                    for (int i = 0; i < mesh.VertexCount; i++)
                    {
                        //add position, after transforming it with accumulated node transform
                        {
                            Vector4 result;
                            Vector3 pos = FromVector(positions[i]);
                            Vector3.Transform(ref pos, ref transform, out result);
                            vertexStream.Write<Vector3>(new Vector3(result.X, result.Y, result.Z));
                        }

                        if (hasColors)
                        {
                            Color vertColor = FromColor(mesh.VertexColorChannels[0][i]);
                            vertexStream.Write<Color>(vertColor);
                        }
                        if (hasNormals)
                        {
                            Vector4 result;
                            Vector3 normal = FromVector(normals[i]);
                            Vector3.Transform(ref normal, ref invTranspose, out result);
                            vertexStream.Write<Vector3>(new Vector3(result.X, result.Y, result.Z));
                        }
                        if (hasTangents)
                        {
                            Vector4 result;
                            Vector3 tangent = FromVector(tangents[i]);
                            Vector3.Transform(ref tangent, ref invTranspose, out result);
                            vertexStream.Write<Vector3>(new Vector3(result.X, result.Y, result.Z));
                        }
                        if (hasBitangents)
                        {
                            Vector4 result;
                            Vector3 biTangent = FromVector(biTangents[i]);
                            Vector3.Transform(ref biTangent, ref invTranspose, out result);
                            vertexStream.Write<Vector3>(new Vector3(result.X, result.Y, result.Z));
                        }
                        if (hasTexCoords)
                        {
                            vertexStream.Write<Vector2>(new Vector2(texCoords[i].X, 1 - texCoords[i].Y));
                        }
                    }

                    vertexStream.Position = 0;

                    //create new vertex buffer
                    var vertexBuffer = new Buffer(device,
                                                    vertexStream,
                                                    new BufferDescription()
                                                    {
                                                        BindFlags = BindFlags.VertexBuffer,
                                                        CpuAccessFlags = CpuAccessFlags.None,
                                                        OptionFlags = ResourceOptionFlags.None,
                                                        SizeInBytes = mesh.VertexCount * vertexSize,
                                                        Usage = ResourceUsage.Default
                                                    }
                                                 );

                    //add it to the mesh
                    modelMesh.VertexBuffer = vertexBuffer;
                    modelMesh.VertexCount = mesh.VertexCount;
                    modelMesh.PrimitiveCount = mesh.FaceCount;

                    //get pointer to indices data
                    uint[] indices = mesh.GetIndices().Select(i => (uint)i).ToArray();

                    //create data stream for indices
                    DataStream indexStream = new DataStream(indices.GetLength(0) * sizeof(uint), true, true);

                    for (int i = 0; i < indices.GetLength(0); i++)
                    {
                        indexStream.Write<uint>(indices[i]);
                    }

                    indexStream.Position = 0;

                    //create new index buffer
                    var indexBuffer = new Buffer(device,
                                                    indexStream,
                                                    new BufferDescription()
                                                    {
                                                        BindFlags = BindFlags.IndexBuffer,
                                                        CpuAccessFlags = CpuAccessFlags.None,
                                                        OptionFlags = ResourceOptionFlags.None,
                                                        SizeInBytes = indices.GetLength(0) * sizeof(uint),
                                                        Usage = ResourceUsage.Default
                                                    }
                                                 );

                    //add it to the mesh
                    modelMesh.IndexBuffer = indexBuffer;
                    modelMesh.IndexCount = indices.GetLength(0);
                }
            }

            //if node has more children process them as well
            for (int i = 0; i < node.ChildCount; i++)
            {
                AddVertexData(model, scene, node.Children[i], device, ref transform);
            }

            transform = previousTransform;
        }

        //some Assimp to SharpDX conversion helpers
        private Matrix FromMatrix(Matrix4x4 mat)
        {
            Matrix m = new Matrix();
            m.M11 = mat.A1;
            m.M12 = mat.A2;
            m.M13 = mat.A3;
            m.M14 = mat.A4;
            m.M21 = mat.B1;
            m.M22 = mat.B2;
            m.M23 = mat.B3;
            m.M24 = mat.B4;
            m.M31 = mat.C1;
            m.M32 = mat.C2;
            m.M33 = mat.C3;
            m.M34 = mat.C4;
            m.M41 = mat.D1;
            m.M42 = mat.D2;
            m.M43 = mat.D3;
            m.M44 = mat.D4;
            return m;
        }

        private Vector3 FromVector(Vector3D vec)
        {
            Vector3 v;
            v.X = vec.X;
            v.Y = vec.Y;
            v.Z = vec.Z;
            return v;
        }

        private Color FromColor(Color4D color)
        {
            Color c;
            c.R = (byte)(color.R * 255);
            c.G = (byte)(color.G * 255);
            c.B = (byte)(color.B * 255);
            c.A = (byte)(color.A * 255);
            return c;
        }

    }
}

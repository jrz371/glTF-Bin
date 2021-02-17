using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.DocObjects;
using Rhino;
using Rhino.FileIO;
using glTFLoader.Schema;
using Rhino.Geometry;
using Rhino.Render;
using Rhino.Display;
using System.Drawing;

namespace glTF_BinExporter
{
    public struct SanitizedRhinoObject
    {
        public Rhino.Geometry.Mesh[] Meshes;
        public Rhino.DocObjects.Material Material;
        public Guid MaterialId;
        public RhinoObject Object;
    }

    class RhinoDocGltfConverter
    {
        public RhinoDocGltfConverter(glTFExportOptions options, IEnumerable<RhinoObject> objects)
        {
            this.options = options;
            this.objects = objects;
        }

        public RhinoDocGltfConverter(glTFExportOptions options, RhinoDoc doc)
        {
            this.options = options;
            this.objects = doc.Objects;
        }

        private IEnumerable<RhinoObject> objects = null;
        private glTFExportOptions options = null;

        private Dictionary<Guid, int> materialsMap = new Dictionary<Guid, int>();

        private gltfSchemaDummy dummy = new gltfSchemaDummy();

        private List<byte> binaryBuffer = new List<byte>();

        public Gltf ConvertToGltf()
        {
            dummy.Scene = 0;
            dummy.Scenes.Add(new gltfSchemaSceneDummy());

            dummy.Asset = new Asset()
            {
                Version = "2.0",
            };

            dummy.Samplers.Add(new Sampler()
            {
                MinFilter = Sampler.MinFilterEnum.LINEAR,
                MagFilter = Sampler.MagFilterEnum.LINEAR,
                WrapS = Sampler.WrapSEnum.REPEAT,
                WrapT = Sampler.WrapTEnum.REPEAT,
            });

            if(options.UseDracoCompression)
            {
                dummy.ExtensionsUsed.Add(Constants.DracoMeshCompressionExtensionTag);
                dummy.ExtensionsRequired.Add(Constants.DracoMeshCompressionExtensionTag);
            }

            var sanitized = SanitizeRhinoObjects(objects);

            foreach(SanitizedRhinoObject sanitizedRhinoObject in sanitized)
            {
                AddObject(sanitizedRhinoObject);
            }

            if(options.UseBinary)
            {
                //have to add the empty buffer for the binary file header
                dummy.Buffers.Add(new glTFLoader.Schema.Buffer()
                {
                    ByteLength = (int)binaryBuffer.Count,
                    Uri = null,
                });
            }

            return dummy.ToSchemaGltf();
        }

        private List<SanitizedRhinoObject> SanitizeRhinoObjects(IEnumerable<RhinoObject> rhinoObjects)
        {
            var rhinoObjectsRes = new List<SanitizedRhinoObject>();

            foreach (var rhinoObject in rhinoObjects)
            {
                var mat = rhinoObject.GetMaterial(true);
                var renderMatId = mat.Id;
                bool isPBR = mat.IsPhysicallyBased;

                // This is always true when called from the Main plugin command, as it uses the same ObjectType array as filter.
                // Keeping it around in case someone calls this from somewhere else.
                var isValidGeometry = Constants.ValidObjectTypes.Contains(rhinoObject.ObjectType);

                if (isValidGeometry && rhinoObject.ObjectType != ObjectType.InstanceReference)
                {
                    // None-block. Just add it to the result list
                    rhinoObjectsRes.Add(new SanitizedRhinoObject
                    {
                        Meshes = GetMeshes(rhinoObject),
                        Material = mat,
                        MaterialId = renderMatId,
                        Object = rhinoObject,
                    });
                }
                else if (rhinoObject.ObjectType == ObjectType.InstanceReference)
                {
                    // Cast to InstanceObject/BlockInstance
                    var instanceObject = (InstanceObject)rhinoObject;
                    // Explode the Block
                    instanceObject.Explode(true, out RhinoObject[] pieces, out ObjectAttributes[] attribs, out Transform[] transforms);

                    // Transform the exploded geo into its correct place
                    foreach (var item in pieces.Zip(transforms, (rObj, trans) => (rhinoObject: rObj, trans)))
                    {
                        var meshes = GetMeshes(item.rhinoObject);

                        foreach (var mesh in meshes)
                        {
                            mesh.Transform(item.trans);
                        }

                        // Add the exploded, transformed geo to the result list
                        rhinoObjectsRes.Add(new SanitizedRhinoObject
                        {
                            Meshes = meshes,
                            Material = mat,
                            MaterialId = renderMatId,
                            Object = rhinoObject,
                        });
                    }
                }
                else
                {
                    // TODO: Should give better error message here.
                    RhinoApp.WriteLine("Unknown geo type encountered.");
                }
            }
            return rhinoObjectsRes;
        }

        private Rhino.Geometry.Mesh[] GetMeshes(RhinoObject rhinoObject)
        {
            Rhino.Geometry.Mesh[] meshes;

            if (rhinoObject.ObjectType == ObjectType.Mesh)
            {
                // Take the Mesh directly from the geo.
                var meshObj = (MeshObject)rhinoObject;
                meshes = new Rhino.Geometry.Mesh[] { meshObj.MeshGeometry };
            }
            else
            {
                // Need to get a Mesh from the None-mesh object. Using the FastRenderMesh here. Could be made configurable.
                // First make sure the internal rhino mesh has been created
                rhinoObject.CreateMeshes(MeshType.Preview, MeshingParameters.FastRenderMesh, true);
                // Then get the internal rhino meshes
                meshes = rhinoObject.GetMeshes(MeshType.Preview);
            }

            if (meshes.Length > 0)
            {
                var mainMesh = meshes[0];
                mainMesh.EnsurePrivateCopy();
                foreach (var mesh in meshes.Skip(1))
                {
                    mainMesh.Append(mesh);
                }

                mainMesh.Weld(0.01);

                mainMesh.UnifyNormals();
                mainMesh.RebuildNormals();

                // Note
                return new Rhino.Geometry.Mesh[] { mainMesh };
            }
            else
            {
                return new Rhino.Geometry.Mesh[] { };
            }
        }

        public byte[] GetBinaryBuffer()
        {
            return binaryBuffer.ToArray();
        }

        public int GetMaterial(Rhino.DocObjects.Material material, Guid materialId)
        {
            if(!materialsMap.TryGetValue(materialId, out int materialIndex))
            {
                RhinoMaterialGltfConverter materialConverter = new RhinoMaterialGltfConverter(options, dummy, binaryBuffer, material);
                materialIndex = materialConverter.AddMaterial();
                materialsMap.Add(materialId, materialIndex);
            }

            return materialIndex;
        }

        int AddObject(SanitizedRhinoObject sanitizedObject)
        {
            RhinoObjectGltfConverter converter = new RhinoObjectGltfConverter(options, dummy, binaryBuffer, sanitizedObject, this);
            return converter.AddObject();
        }

    }
}

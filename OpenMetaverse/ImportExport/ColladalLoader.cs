﻿/*
 * Copyright (c) 2006-2014, openmetaverse.org
 * All rights reserved.
 *
 * - Redistribution and use in source and binary forms, with or without 
 *   modification, are permitted provided that the following conditions are met:
 *
 * - Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 * - Neither the name of the openmetaverse.org nor the names 
 *   of its contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" 
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF 
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
 * POSSIBILITY OF SUCH DAMAGE.
 */


using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using OpenMetaverse.Rendering;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Http;

namespace OpenMetaverse.ImportExport
{
    public class ColladaLoader
    {
        COLLADA Model;
        static XmlSerializer Serializer = null;
        List<Node> Nodes;
        List<ModelMaterial> Materials;

        class Node
        {
            public Matrix4 Transform = Matrix4.Identity;
            public string Name;
            public string ID;
            public string MeshID;
        }

        public List<ModelPrim> Load(string filename)
        {
            try
            {
                // Create an instance of the XmlSerializer specifying type and namespace.
                if (Serializer == null)
                {
                    Serializer = new XmlSerializer(typeof(COLLADA));
                }

                // A FileStream is needed to read the XML document.
                FileStream fs = new FileStream(filename, FileMode.Open);
                XmlReader reader = XmlReader.Create(fs);
                Model = (COLLADA)Serializer.Deserialize(reader);
                fs.Close();
                return Parse();
            }
            catch (Exception ex)
            {
                Logger.Log("Failed parsing collada file: " + ex.Message, Helpers.LogLevel.Error, ex);
                return new List<ModelPrim>();
            }
        }


        ModelMaterial ExtractMaterial(object diffuse)
        {
            ModelMaterial ret = new ModelMaterial();
            if (diffuse is common_color_or_texture_typeColor)
            {
                var col = (common_color_or_texture_typeColor)diffuse;
                ret.DiffuseColor = new Color4((float)col.Values[0], (float)col.Values[1], (float)col.Values[2], (float)col.Values[3]);
            }
            else if (diffuse is common_color_or_texture_typeTexture)
            {
                var tex = (common_color_or_texture_typeTexture)diffuse;
                ret.Texture = tex.texcoord;
            }
            return ret;

        }

        void ParseMaterials()
        {

            if (Model == null) return;

            Materials = new List<ModelMaterial>();

            // Material -> effect mapping
            Dictionary<string, string> matEffect = new Dictionary<string, string>();
            List<ModelMaterial> tmpEffects = new List<ModelMaterial>();

            // Image ID -> filename mapping
            Dictionary<string, string> imgMap = new Dictionary<string, string>();

            foreach (var item in Model.Items)
            {
                if (item is library_images)
                {
                    var images = (library_images)item;
                    if (images.image != null)
                    {
                        foreach (var image in images.image)
                        {
                            var img = (image)image;
                            string ID = img.id;
                            if (img.Item is string)
                            {
                                imgMap[ID] = (string)img.Item;
                            }
                        }
                    }
                }
            }

            foreach (var item in Model.Items)
            {
                if (item is library_materials)
                {
                    var materials = (library_materials)item;
                    if (materials.material != null)
                    {
                        foreach (var material in materials.material)
                        {
                            var ID = material.id;
                            if (material.instance_effect != null)
                            {
                                if (!string.IsNullOrEmpty(material.instance_effect.url))
                                {
                                    matEffect[material.instance_effect.url.Substring(1)] = ID;
                                }
                            }
                        }
                    }
                }
            }

            foreach (var item in Model.Items)
            {
                if (item is library_effects)
                {
                    var effects = (library_effects)item;
                    if (effects.effect != null)
                    {
                        foreach (var effect in effects.effect)
                        {
                            string ID = effect.id;
                            foreach (var effItem in effect.Items)
                            {
                                if (effItem is effectFx_profile_abstractProfile_COMMON)
                                {
                                    var teq = ((effectFx_profile_abstractProfile_COMMON)effItem).technique;
                                    if (teq != null)
                                    {
                                        if (teq.Item is effectFx_profile_abstractProfile_COMMONTechniquePhong)
                                        {
                                            var shader = (effectFx_profile_abstractProfile_COMMONTechniquePhong)teq.Item;
                                            if (shader.diffuse != null)
                                            {
                                                var material = ExtractMaterial(shader.diffuse.Item);
                                                material.ID = ID;
                                                tmpEffects.Add(material);
                                            }
                                        }
                                        else if (teq.Item is effectFx_profile_abstractProfile_COMMONTechniqueLambert)
                                        {
                                            var shader = (effectFx_profile_abstractProfile_COMMONTechniqueLambert)teq.Item;
                                            if (shader.diffuse != null)
                                            {
                                                var material = ExtractMaterial(shader.diffuse.Item);
                                                material.ID = ID;
                                                tmpEffects.Add(material);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            foreach (var effect in tmpEffects)
            {
                if (matEffect.ContainsKey(effect.ID))
                {
                    effect.ID = matEffect[effect.ID];
                    if (!string.IsNullOrEmpty(effect.Texture))
                    {
                        if (imgMap.ContainsKey(effect.Texture))
                        {
                            effect.Texture = imgMap[effect.Texture];
                        }
                    }
                    Materials.Add(effect);
                }
            }
        }

        void ParseVisualScene()
        {
            Nodes = new List<Node>();
            if (Model == null) return;

            foreach (var item in Model.Items)
            {
                if (item is library_visual_scenes)
                {
                    var scene = ((library_visual_scenes)item).visual_scene[0];
                    foreach (var node in scene.node)
                    {
                        Node n = new Node();
                        n.ID = node.id;

                        // Try finding matrix
                        foreach (var i in node.Items)
                        {
                            if (i is matrix)
                            {
                                var m = (matrix)i;
                                for (int a = 0; a < 4; a++)
                                    for (int b = 0; b < 4; b++)
                                    {
                                        n.Transform[b, a] = (float)m.Values[a * 4 + b];
                                    }
                            }
                        }

                        // Find geopmetry and material
                        if (node.instance_geometry != null && node.instance_geometry.Length > 0)
                        {
                            var instGeom = node.instance_geometry[0];
                            if (!string.IsNullOrEmpty(instGeom.url))
                            {
                                n.MeshID = instGeom.url.Substring(1);
                            }

                        }

                        Nodes.Add(n);
                    }
                }
            }
        }

        List<ModelPrim> Parse()
        {
            var Prims = new List<ModelPrim>();

            float DEG_TO_RAD = 0.017453292519943295769236907684886f;

            if (Model == null) return Prims;

            Matrix4 tranform = Matrix4.Identity;

            UpAxisType upAxis = UpAxisType.Y_UP;

            var asset = Model.asset;
            if (asset != null)
            {
                upAxis = asset.up_axis;
                if (asset.unit != null)
                {
                    float meter = (float)asset.unit.meter;
                    tranform[0, 0] = meter;
                    tranform[1, 1] = meter;
                    tranform[2, 2] = meter;
                }
            }

            Matrix4 rotation = Matrix4.Identity;

            if (upAxis == UpAxisType.X_UP)
            {
                rotation = Matrix4.CreateFromEulers(0.0f, 90.0f * DEG_TO_RAD, 0.0f);
            }
            else if (upAxis == UpAxisType.Y_UP)
            {
                rotation = Matrix4.CreateFromEulers(90.0f * DEG_TO_RAD, 0.0f, 0.0f);
            }

            rotation = rotation * tranform;
            tranform = rotation;

            ParseVisualScene();
            ParseMaterials();

            foreach (var item in Model.Items)
            {
                if (item is library_geometries)
                {
                    var geometries = (library_geometries)item;
                    foreach (var geo in geometries.geometry)
                    {
                        var mesh = geo.Item as mesh;
                        if (mesh == null) continue;
                        var prim = new ModelPrim();
                        prim.ID = geo.id;
                        Prims.Add(prim);
                        Matrix4 primTranform = tranform;

                        var node = Nodes.Find(n => n.MeshID == prim.ID);
                        if (node != null)
                        {
                            primTranform = primTranform * node.Transform;
                        }

                        AddPositions(mesh, prim, primTranform);

                        foreach (var mitem in mesh.Items)
                        {
                            if (mitem is polylist)
                            {
                                AddFacesFromPolyList((polylist)mitem, mesh, prim);
                            }
                        }

                        prim.CreateAsset(UUID.Zero);

                    }
                }
            }

            return Prims;
        }

        source FindSource(source[] sources, string id)
        {
            id = id.Substring(1);

            foreach (var src in sources)
            {
                if (src.id == id)
                    return src;
            }
            return null;
        }

        void AddPositions(mesh mesh, ModelPrim prim, Matrix4 transform)
        {
            prim.Positions = new List<Vector3>();
            source posSrc = FindSource(mesh.source, mesh.vertices.input[0].source);
            double[] posVals = ((float_array)posSrc.Item).Values;

            for (int i = 0; i < posVals.Length / 3; i++)
            {
                Vector3 pos = new Vector3((float)posVals[i * 3], (float)posVals[i * 3 + 1], (float)posVals[i * 3 + 2]);
                pos = Vector3.Transform(pos, transform);
                prim.Positions.Add(pos);
            }

            prim.BoundMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            prim.BoundMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var pos in prim.Positions)
            {
                if (pos.X > prim.BoundMax.X) prim.BoundMax.X = pos.X;
                if (pos.Y > prim.BoundMax.Y) prim.BoundMax.Y = pos.Y;
                if (pos.Z > prim.BoundMax.Z) prim.BoundMax.Z = pos.Z;

                if (pos.X < prim.BoundMin.X) prim.BoundMin.X = pos.X;
                if (pos.Y < prim.BoundMin.Y) prim.BoundMin.Y = pos.Y;
                if (pos.Z < prim.BoundMin.Z) prim.BoundMin.Z = pos.Z;
            }

            prim.Scale = prim.BoundMax - prim.BoundMin;
            prim.Position = prim.BoundMin + (prim.Scale / 2);

            // Fit vertex positions into identity cube -0.5 .. 0.5
            for (int i = 0; i < prim.Positions.Count; i++)
            {
                Vector3 pos = prim.Positions[i];
                pos = new Vector3(
                    prim.Scale.X == 0 ? 0 : ((pos.X - prim.BoundMin.X) / prim.Scale.X) - 0.5f,
                    prim.Scale.Y == 0 ? 0 : ((pos.Y - prim.BoundMin.Y) / prim.Scale.Y) - 0.5f,
                    prim.Scale.Z == 0 ? 0 : ((pos.Z - prim.BoundMin.Z) / prim.Scale.Z) - 0.5f
                    );
                prim.Positions[i] = pos;
            }

        }

        int[] StrToArray(string s)
        {
            string[] vals = Regex.Split(s.Trim(), @"\s+");
            int[] ret = new int[vals.Length];

            for (int i = 0; i < ret.Length; i++)
            {
                int.TryParse(vals[i], out ret[i]);
            }

            return ret;
        }

        void AddFacesFromPolyList(polylist list, mesh mesh, ModelPrim prim)
        {
            string material = list.material;
            source posSrc = null;
            source normalSrc = null;
            source uvSrc = null;

            ulong stride = 0;
            int posOffset = -1;
            int norOffset = -1;
            int uvOffset = -1;

            foreach (var inp in list.input)
            {
                stride = Math.Max(stride, inp.offset);

                if (inp.semantic == "VERTEX")
                {
                    posSrc = FindSource(mesh.source, mesh.vertices.input[0].source);
                    posOffset = (int)inp.offset;
                }
                else if (inp.semantic == "NORMAL")
                {
                    normalSrc = FindSource(mesh.source, inp.source);
                    norOffset = (int)inp.offset;
                }
                else if (inp.semantic == "TEXCOORD")
                {
                    uvSrc = FindSource(mesh.source, inp.source);
                    uvOffset = (int)inp.offset;
                }
            }

            stride += 1;

            if (posSrc == null) return;

            var vcount = StrToArray(list.vcount);
            var idx = StrToArray(list.p);

            Vector3[] normals = null;
            if (normalSrc != null)
            {
                var norVal = ((float_array)normalSrc.Item).Values;
                normals = new Vector3[norVal.Length / 3];

                for (int i = 0; i < normals.Length; i++)
                {
                    normals[i] = new Vector3((float)norVal[i * 3 + 0], (float)norVal[i * 3 + 1], (float)norVal[i * 3 + 2]);
                }

            }

            Vector2[] uvs = null;
            if (uvSrc != null)
            {
                var uvVal = ((float_array)uvSrc.Item).Values;
                uvs = new Vector2[uvVal.Length / 2];

                for (int i = 0; i < uvs.Length; i++)
                {
                    uvs[i] = new Vector2((float)uvVal[i * 2 + 0], (float)uvVal[i * 2 + 1]);
                }

            }

            ModelFace face = new ModelFace();
            face.MaterialID = list.material;
            ModelMaterial mat = Materials.Find(m => m.ID == face.MaterialID);
            if (mat != null)
            {
                face.Material = mat;
            }

            int curIdx = 0;

            for (int i = 0; i < vcount.Length; i++)
            {
                var npoly = vcount[i];
                if (npoly != 3)
                {
                    throw new InvalidDataException("Only triangulated meshes supported");
                }

                int v1i = idx[curIdx + posOffset + (int)stride * 0];
                int v2i = idx[curIdx + posOffset + (int)stride * 1];
                int v3i = idx[curIdx + posOffset + (int)stride * 2];

                Vertex v1 = new Vertex();
                Vertex v2 = new Vertex();
                Vertex v3 = new Vertex();

                v1.Position = prim.Positions[v1i];
                v2.Position = prim.Positions[v2i];
                v3.Position = prim.Positions[v3i];

                if (normals != null)
                {
                    v1.Normal = normals[idx[curIdx + norOffset + (int)stride * 0]];
                    v2.Normal = normals[idx[curIdx + norOffset + (int)stride * 1]];
                    v3.Normal = normals[idx[curIdx + norOffset + (int)stride * 2]];
                }

                if (uvs != null)
                {
                    v1.TexCoord = uvs[idx[curIdx + uvOffset + (int)stride * 0]];
                    v2.TexCoord = uvs[idx[curIdx + uvOffset + (int)stride * 1]];
                    v3.TexCoord = uvs[idx[curIdx + uvOffset + (int)stride * 2]];
                }

                face.AddVertex(v1);
                face.AddVertex(v2);
                face.AddVertex(v3);

                curIdx += (int)stride * npoly;
            }

            prim.Faces.Add(face);


        }
    }
}

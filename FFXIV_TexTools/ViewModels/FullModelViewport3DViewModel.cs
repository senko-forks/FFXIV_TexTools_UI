// FFXIV TexTools
// Copyright © 2020 Rafael Gonzalez - All Rights Reserved
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using FFXIV_TexTools.Custom;
using FFXIV_TexTools.Helpers;
using FFXIV_TexTools.Resources;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Animations;
using HelixToolkit.Wpf.SharpDX.Model;
using HelixToolkit.Wpf.SharpDX.Model.Scene;
using Newtonsoft.Json;
using SharpDX;
using SharpDX.Direct3D11;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX.Cameras;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.Models.DataContainers;
using xivModdingFramework.Models.Helpers;
using Color = SharpDX.Color;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using SRGBVector4 = System.Numerics.Vector4;
using FFXIV_TexTools.Views;
using System.Diagnostics;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace FFXIV_TexTools.ViewModels
{
    public class FullModelViewport3DViewModel : Viewport3DViewModel
    {

        public Dictionary<string, DisplayedModelData> shownModels = new Dictionary<string, DisplayedModelData>();

        public FullModelViewport3DViewModel()
        {
        }

        #region Public Methods

        /// <summary>
        /// Updates or Adds the Model to the viewport
        /// </summary>
        /// <param name="model">The TexTools Model</param>
        /// <param name="textureDataDictionary">The textures associated with the model</param>
        /// <param name="item">The item for the model</param>
        /// <param name="modelRace">The race of the model</param>
        /// <param name="targetRace">The target race the model should be</param>
        public async Task UpdateModel(TTModel model, Dictionary<int, ModelTextureData> textureDataDictionary, IItemModel item, XivRace modelRace, XivRace targetRace)
        {
            // If target race is different than the model race Apply racial deforms
            try
            {
                if (modelRace != targetRace)
                {
                    await ModelModifiers.RaceConvertRecursive(model, targetRace, modelRace);
                }
            }
            catch(Exception ex)
            {
                Trace.WriteLine(ex);
            }
            var slot = ViewHelpers.GetModelSlot(model.Source);

            SharpDX.BoundingBox? boundingBox = null;
            ModelModifiers.CalculateTangents(model);

            // Remove any existing models of the same item type
            RemoveSlot(slot);

            var totalMeshCount = model.MeshGroups.Count;

            for (var i = 0; i < totalMeshCount; i++)
            {
                var meshGeometry3D = GetMeshGeometry(model, i);

                var textureData = textureDataDictionary[model.GetMaterialIndex(i)];

                TextureModel diffuse = null, specular = null, normal = null, alpha = null, emissive = null;

                if (textureData.Diffuse != null && textureData.Diffuse.Length > 0)
                    diffuse = new TextureModel(textureData.Diffuse, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);

                if (textureData.Specular != null && textureData.Specular.Length > 0)
                    specular = new TextureModel(textureData.Specular, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);

                if (textureData.Normal != null && textureData.Normal.Length > 0)
                    normal = new TextureModel(textureData.Normal, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);

                if (textureData.Alpha != null && textureData.Alpha.Length > 0)
                    alpha = new TextureModel(textureData.Alpha, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);

                if (textureData.Emissive != null && textureData.Emissive.Length > 0)
                    emissive = new TextureModel(textureData.Emissive, SharpDX.DXGI.Format.R8G8B8A8_UNorm, textureData.Width, textureData.Height);

                var material = new PhongMaterial
                {
                    DiffuseColor = PhongMaterials.ToColor(1, 1, 1, 1),
                    SpecularShininess = 1f,
                    DiffuseMap = diffuse,
                    DiffuseAlphaMap = alpha,
                    SpecularColorMap = specular,
                    NormalMap = normal,
                    EmissiveMap = emissive
                };

                // Geometry that contains skeleton data
                var smgm3d = new CustomMeshGeometryModel3D
                {
                    Geometry = meshGeometry3D,
                    Material = material,
                    Source = model.Source,
                };

                boundingBox = meshGeometry3D.Bound;

                smgm3d.CullMode = textureData.RenderBackfaces ? CullMode.None : CullMode.Back;

                Models.Add(smgm3d);
            }
            var center = boundingBox.GetValueOrDefault().Center;


            // Keep track of the models displayed in the viewport
            shownModels.Add(slot, new DisplayedModelData{TtModel = model, ItemModel = item, ModelTextureData = textureDataDictionary});
        }

        /// <summary>
        /// Updates all models to the new skeleton
        /// </summary>
        /// <param name="previousRace">The original or previous race of the model</param>
        /// <param name="targetRace">The target race for the skeleton and model</param>
        public async void UpdateSkeleton(XivRace previousRace, XivRace targetRace)
        {
            var shownModelList = new List<string>();

            foreach (var model in shownModels)
            {
                shownModelList.Add(model.Key);
            }

            try
            {
                // Apply racial transforms
                // This pretty much replaces every model by deleting and recreating them with the target race deforms
                foreach (var model in shownModelList)
                {
                    await UpdateModel(shownModels[model].TtModel, shownModels[model].ModelTextureData, shownModels[model].ItemModel, previousRace, targetRace);
                }
            }
            catch(Exception ex)
            {
                //ViewHelpers.ShowError(_modelViewModel, "Skeleton Update Errror", "An error occurred while updating the skeleon:\n\n" + ex.Message);
                Trace.WriteLine(ex);
            }
        }

        /// <summary>
        /// Updates all models to the new skeleton
        /// </summary>
        /// <param name="previousRace">The original or previous race of the model</param>
        /// <param name="targetRace">The target race for the skeleton and model</param>
        public async void UpdateSkin(XivRace race)
        {
            var shownModelList = new List<string>();
            try
            {

                foreach (var model in shownModels)
                {
                    shownModelList.Add(model.Key);
                }

                foreach (var model in shownModelList)
                {
                    await UpdateModel(shownModels[model].TtModel, shownModels[model].ModelTextureData, shownModels[model].ItemModel, race, race);
                }
            }
            catch(Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }


        /// <summary>
        /// Removes a model from the viewport
        /// </summary>
        public void RemoveSlot(string slot)
        {
            // Determine which models needs to be removed
            var modelsToRemove = new List<CustomMeshGeometryModel3D>();

            if (!string.IsNullOrEmpty(slot))
            {
                foreach (var displayedModel in Models)
                {
                    var model = displayedModel as CustomMeshGeometryModel3D;
                    var s = ViewHelpers.GetModelSlot(model.Source);
                    if (slot == s)
                    {
                        modelsToRemove.Add(model);

                        shownModels.Remove(slot);
                    }
                }
            }
            

            foreach (var model in modelsToRemove)
            {
                // Remove the model
                model.Dispose();
                Models.Remove(model);
            }
        }

        /// <summary>
        /// Clears all models and skeleton
        /// </summary>
        public void ClearAll()
        {
            foreach(var model in Models)
            {
                model.Dispose();
            }

            Models.Clear();
            shownModels.Clear();
        }

        /// <summary>
        /// Clean up when window is closed
        /// </summary>
        public void CleanUp()
        {
            ClearAll();
        }

        #endregion

        #region Private Methods
        /// <summary>
        /// Gets the Mesh Geometry
        /// </summary>
        /// <remarks>
        /// This is mostly the same as the single model viewport but contains bone data
        /// </remarks>
        /// <param name="model">The model to get the geometry from</param>
        /// <param name="meshGroupId">The mesh group ID</param>
        /// <returns>The Skinned Mesh Geometry</returns>
        private MeshGeometry3D GetMeshGeometry(TTModel model, int meshGroupId)
        {
            var group = model.MeshGroups[meshGroupId];
            var mg = new MeshGeometry3D
            {
                Positions = new Vector3Collection((int)group.VertexCount),
                Normals = new Vector3Collection((int)group.VertexCount),
                Colors = new Color4Collection((int)group.VertexCount),
                TextureCoordinates = new Vector2Collection((int)group.VertexCount),
                BiTangents = new Vector3Collection((int)group.VertexCount),
                Tangents = new Vector3Collection((int)group.VertexCount),
                Indices = new IntCollection((int)group.IndexCount),
            };

            var indexCount = 0;
            var vertCount = 0;

            foreach (var p in group.Parts)
            {
                foreach (var v in p.Vertices)
                {

                    // I don't think our current shader actually utilizes this data anyways
                    // but may as well include it correctly.
                    var color = new Color4();
                    color.Red = v.VertexColor[0] / 255f;
                    color.Green = v.VertexColor[1] / 255f;
                    color.Blue = v.VertexColor[2] / 255f;
                    color.Alpha = v.VertexColor[3] / 255f;

                    mg.Positions.Add(v.Position);
                    mg.Normals.Add(v.Normal);
                    mg.TextureCoordinates.Add(v.UV1);
                    mg.Colors.Add(color);
                    mg.BiTangents.Add(v.Binormal);
                    mg.Tangents.Add(v.Tangent);
                }

                foreach (var vertexId in p.TriangleIndices)
                {
                    // Get the bone indices and weights for current index
                    var boneIndices = p.Vertices[vertexId].BoneIds;
                    var boneWeights = p.Vertices[vertexId].Weights;
                    var bw1 = boneWeights[0] / 255f;
                    var bw2 = boneWeights[1] / 255f;
                    var bw3 = boneWeights[2] / 255f;
                    var bw4 = boneWeights[3] / 255f;

                    // Have to bump these to account for merging the lists together.
                    mg.Indices.Add(vertCount + vertexId);
                }

                vertCount += p.Vertices.Count;
                indexCount += p.TriangleIndices.Count;
            }
            return mg;
        }


        /// <summary>
        /// Creates the composite bone dictionary for all the models.
        /// </summary>
        /// <returns></returns>
        private Dictionary<string, SkeletonData> GetBoneDictionary(XivRace race)
        {
            // Just build a quick and dirty temp model we can use to call the full bone heirarchy function.
            var tempModel = new TTModel();
            var models = new HashSet<string>();
            foreach(var kv in shownModels)
            {
                models.Add(kv.Value.TtModel.Source);
            }

            var boneDict = TTModel.ResolveFullBoneHeirarchy(race, models.ToList());

            // Fill in any missing bones that we couldn't otherwise figure out, if possible.
            var missingBones = new List<string>();

            var boneList = tempModel.Bones;
            foreach (var bone in boneList)
            {
                if (!boneDict.ContainsKey(bone)) {
                    // Create a dummy bone for anything missing.
                    boneDict.Add(bone, new SkeletonData() { 
                        BoneName = bone, 
                        BoneParent = 0, 
                        BoneNumber = boneDict.Select(x => x.Value.BoneNumber).Max() + 1, 
                        InversePoseMatrix = Matrix.Identity.ToArray(), 
                        PoseMatrix = Matrix.Identity.ToArray()
                    });
                }
            }

            return boneDict;
        }

        /// <summary>
        /// Creates the Bones to be used in the format Helix Toolkit uses
        /// </summary>
        /// <param name="boneList">The list of bones in the model</param>
        /// <param name="targetRace">The target race to get the bones from</param>
        /// <returns>A list of Bone structures used by Helix Toolkit</returns>
        private List<Bone> MakeHelixBones(XivRace targetRace)
        {
            // Get the skeleton, including all EX bones.
            var boneDict = GetBoneDictionary(targetRace);

            // Functions below expect bone dictionary by numer.
            var numericBoneDict = new Dictionary<int, SkeletonData>();
            foreach (var entry in boneDict)
            {
                numericBoneDict.Add(entry.Value.BoneNumber, entry.Value);
            }

            // Add only the bones that are contained in the model including all parent bones
            var bonesInModel = boneDict.Values.ToList();

            foreach (var bone in boneDict)
            {
                bonesInModel.Add(bone.Value);

                AddBones(numericBoneDict, bonesInModel, bone.Value);
            }

            // Create a bone list with the bones for the model in helix toolkit format
            var helixBoneList = new List<Bone>();

            foreach (var bone in bonesInModel)
            {
                var bp = new Matrix(bone.InversePoseMatrix);
                bp.Invert();

                helixBoneList.Add(new Bone
                {
                    BindPose = bp,
                    Name = bone.BoneName,
                    ParentIndex = bone.BoneParent

                });
            }

            return helixBoneList;
        }

        /// <summary>
        /// Adds the parent bones all the way to root using recursive calls
        /// </summary>
        /// <param name="skelDict">Dictionary containing all skeleton data by bone number</param>
        /// <param name="skelData">List containing bones to be used for the model</param>
        /// <param name="bone">The bone being added</param>
        private void AddBones(Dictionary<int, SkeletonData> skelDict, List<SkeletonData> skelData, SkeletonData bone)
        {
            // Determine whether the parent has already been added
            var parentAlreadyAdded = skelData.Any(b => b.BoneNumber == bone.BoneParent);

            // This would be the root bone
            if (bone.BoneParent == -1)
            {
                skelData.Add(bone);
                parentAlreadyAdded = true;
            }
            
            // If the parent has not been added, make a recursive call with the parent bone
            if (!parentAlreadyAdded)
            {
                var parent = skelDict[bone.BoneParent];
                AddBones(skelDict, skelData, parent);

                // Update the bone with the new parent bone index
                var newParent = (from b in skelData where b.BoneName == parent.BoneName select b).FirstOrDefault();
                bone.BoneParent = skelData.IndexOf(newParent);
                skelData.Add(bone);
            }
            // If the parent already exists, and it's not the root bone, just add the bone 
            else if (bone.BoneParent != -1)
            {
                var parent = skelDict[bone.BoneParent];

                // Update the bone with the new parent bone index
                var newParent = (from b in skelData where b.BoneName == parent.BoneName select b).FirstOrDefault();
                bone.BoneParent = skelData.IndexOf(newParent);

                skelData.Add(bone);
            }
        }

        /// <summary>
        /// Gets the matrices for the bones used in the model
        /// </summary>
        /// <param name="boneList">List of bones used in the model</param>
        /// <param name="targetRace">Target Race to get the bone data for</param>
        /// <returns>A matrix array containing the pose data for each bone</returns>
        private Matrix[] GetMatrices(XivRace targetRace)
        {
            // Get the skeleton, including all EX bones.
            var boneDict = GetBoneDictionary(targetRace);

            var matrixList = new List<Matrix>();
            foreach (var kv in boneDict)
            {
                var matrix = new Matrix(kv.Value.InversePoseMatrix);
                matrix.Invert();

                matrixList.Add(matrix);
            }

            return matrixList.ToArray();
        }


        #endregion

        public class DisplayedModelData
        {
            public TTModel TtModel { get; set; }
            public IItemModel ItemModel { get; set; }
            public Dictionary<int, ModelTextureData> ModelTextureData { get; set; }
        }
    }
}

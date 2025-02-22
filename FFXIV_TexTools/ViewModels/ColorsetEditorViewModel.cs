﻿using FFXIV_TexTools.Controls;
using FFXIV_TexTools.Helpers;
using HelixToolkit.Wpf.SharpDX;
using MahApps.Metro.Controls;
using SharpDX;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using xivModdingFramework.Cache;
using xivModdingFramework.General;
using xivModdingFramework.Materials.DataContainers;
using xivModdingFramework.Materials.FileTypes;
using xivModdingFramework.Textures.DataContainers;
using xivModdingFramework.Textures.FileTypes;

namespace FFXIV_TexTools.ViewModels
{
    public class ColorsetEditorViewModel : INotifyPropertyChanged
    {
        ColorsetEditorControl _view;

        private static XivTex TileTextureNormal;
        private static XivTex TileTextureDiffuse;

        // SharpDX 3D Model Viewing Stuff, which is really all this VM is used for.
        private Viewport3DX _viewport;

        public ObservableElement3DCollection Models { get; } = new ObservableElement3DCollection();
        public Camera Camera { get; set; }
        public EffectsManager EffectsManager { get; }

        private XivMtrl _mtrl;
        private int RowId;

        private StainingTemplateFile DyeTemplateFile;

        private List<Half[]> RowData;

        private bool DawnTrail
        {
            get
            {
                if (_mtrl == null) return true;

                if(_mtrl.ColorSetData.Count >= 1024)
                {
                    return true;
                }
                return false;
            }
        }

        private bool LegacyShader
        {
            get
            {
                if(_mtrl == null) return false;  

                if(DawnTrail)
                {
                    return _mtrl.ShaderPack == ShaderHelpers.EShaderPack.CharacterLegacy;
                }
                else
                {
                    return true;
                }
            }
        }

        public ColorsetEditorViewModel(ColorsetEditorControl view)
        {
            _view = view;

            // Eat exception to not immediately crash in VirtualBox
            try
            {
                EffectsManager = new DefaultEffectsManager();
            } catch { }
            Camera = new PerspectiveCamera();
        }

        bool _NeedLights = true;

        public async Task SetMaterial(XivMtrl mtrl, StainingTemplateFile dyeFile) {
            _mtrl = mtrl;
            DyeTemplateFile = dyeFile;

            _viewport = _view.ColorsetRowViewport;
            _viewport.BackgroundColor = System.Windows.Media.Colors.Gray;
            _viewport.Background = Brushes.Gray;

            if (_NeedLights) {


                _NeedLights = false;
            }

            _viewport.Camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
            _viewport.Camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, -1);
            _viewport.Camera.Position = new System.Windows.Media.Media3D.Point3D(0, 0, 3);



            if (TileTextureNormal == null)
            {
                var _tex = new Tex(XivCache.GameInfo.GameDirectory);
                try
                {
#if DAWNTRAIL
                    TileTextureNormal = await _tex.GetXivTex("chara/common/texture/tile_norm_array.tex");
                    // This is not the correct usage, but works for the moment.
                    TileTextureDiffuse = await _tex.GetXivTex("chara/common/texture/tile_orb_array.tex");
#else
                    // Endwalker tile materials.
                    TileTextureNormal = await _tex.GetXivTex("chara/common/texture/-tile_n.tex");
                    TileTextureDiffuse = await _tex.GetXivTex("chara/common/texture/-tile_d.tex");
#endif
                }
                catch
                {
                }
            }


        }

        public async Task SetColorsetRow(int row, int columnCount, int dyeId = -1)
        {
            if (_mtrl == null) return;

            try
            {
                Models.Clear();
                RowId = row;

                var offset = RowId * columnCount * 4;
                RowData = new List<Half[]>(columnCount);
                for (int i = 0; i < columnCount; i++)
                {
                    var arr = new Half[4];
                    RowData.Add(arr);
                    for (int z = 0; z < 4; z++)
                    {
                        arr[z] = _mtrl.ColorSetData[offset];
                        offset++;
                    }
                }

                var mg3 = MakeCube();

                var dMax = Math.Max(1.0f, Math.Max(RowData[0][0], Math.Max(RowData[0][1], RowData[0][2])));
                var sMax = Math.Max(1.0f, Math.Max(RowData[0][0], Math.Max(RowData[0][1], RowData[0][2])));
                var eMax = Math.Max(1.0f, Math.Max(RowData[0][0], Math.Max(RowData[0][1], RowData[0][2])));

                var dColor = new SharpDX.Color(
                        (byte)Math.Round(Math.Sqrt(RowData[0][0] / dMax) * 255f),
                        (byte)Math.Round(Math.Sqrt(RowData[0][1] / dMax) * 255f),
                        (byte)Math.Round(Math.Sqrt(RowData[0][2] / dMax) * 255f));
                var sColor = new SharpDX.Color(
                        (byte)Math.Round(Math.Sqrt(RowData[1][0] / sMax) * 255f),
                        (byte)Math.Round(Math.Sqrt(RowData[1][1] / sMax) * 255f),
                        (byte)Math.Round(Math.Sqrt(RowData[1][2] / sMax) * 255f));

                var lmMaterial = new PhongMaterial()
                {
                    AmbientColor = SharpDX.Color.Gray,
                    DiffuseColor = dColor,
                    SpecularColor = sColor
                };

                if (RowData[2][0] != 0 || RowData[2][1] != 0 || RowData[2][2] != 0)
                {
                    lmMaterial.EmissiveColor = new SharpDX.Color(
                        (byte)Math.Round(Math.Sqrt(RowData[2][0] / eMax) * 255f),
                        (byte)Math.Round(Math.Sqrt(RowData[2][1] / eMax) * 255f),
                        (byte)Math.Round(Math.Sqrt(RowData[2][2] / eMax) * 255f));
                }


                float glossVal = RowData[1][3];
                float specularPower = RowData[0][3];

                if (DawnTrail)
                {
                    // Values flipped in Dawntrail.
                    glossVal = RowData[0][3];
                    specularPower = RowData[1][3];
                }

                if (dyeId >= 0 && dyeId < 128 && _mtrl.ColorSetDyeData != null)
                {
                    var templateId = STM.GetTemplateKeyFromMaterialData(_mtrl, RowId);
                    var template = DyeTemplateFile.GetTemplate(templateId);


                    uint data;
                    if (_mtrl.ColorSetDyeData.Length > 32)
                    {
                        var dyeRowSize = 4;
                        data = BitConverter.ToUInt32(_mtrl.ColorSetDyeData, dyeRowSize * RowId);
                    }
                    else
                    {
                        var dyeRowSize = 2;
                        data = BitConverter.ToUInt16(_mtrl.ColorSetDyeData, dyeRowSize * RowId);
                    }

                    if(template != null && templateId != 0)
                    {

                        bool useDiffuse = (data & 0x01) > 0;
                        bool useSpecular = (data & 0x02) > 0;
                        bool useEmissive = (data & 0x04) > 0;

                        bool useSpecPower = (data & 0x08) > 0;
                        bool useGloss = (data & 0x10) > 0;

                        var diffuse = template.GetDiffuseData(dyeId);
                        var spec = template.GetSpecularData(dyeId);
                        var emissive = template.GetEmissiveData(dyeId);
                        var gloss = template.GetGlossData(dyeId);
                        var specPower = template.GetSpecularPowerData(dyeId);

                        if (useDiffuse && diffuse != null)
                        {
                            var max = Math.Max(1.0f, Math.Max(diffuse[0], Math.Max(diffuse[1], diffuse[2])));

                            lmMaterial.DiffuseColor = new SharpDX.Color(
                            (byte)Math.Round((diffuse[0] / max) * 255f),
                            (byte)Math.Round((diffuse[1] / max) * 255f),
                            (byte)Math.Round((diffuse[2] / max) * 255f));
                        }

                        if (useSpecular && spec != null)
                        {
                            var max = Math.Max(1.0f, Math.Max(spec[0], Math.Max(spec[1], spec[2])));

                            lmMaterial.SpecularColor = new SharpDX.Color(
                            (byte)Math.Round((spec[0] / max) * 255f),
                            (byte)Math.Round((spec[1] / max) * 255f),
                            (byte)Math.Round((spec[2] / max) * 255f));
                        }

                        if (useEmissive && emissive != null)
                        {
                            var max = Math.Max(1.0f, Math.Max(emissive[0], Math.Max(emissive[1], emissive[2])));

                            lmMaterial.EmissiveColor = new SharpDX.Color(
                            (byte)Math.Round((emissive[0] / max) * 255f),
                            (byte)Math.Round((emissive[1] / max) * 255f),
                            (byte)Math.Round((emissive[2] / max) * 255f));
                        }

                        if(useGloss && LegacyShader && gloss != null)
                        {
                            glossVal = gloss[0];
                        }
                        
                        if(useSpecPower && LegacyShader && specPower != null)
                        {
                            specularPower = specPower[0];
                        }
                                                
                    }
                }

                // This is some arbitrary math to make the gloss more or less reflected in the visual.
                glossVal = (glossVal * glossVal) * 10;
                lmMaterial.SpecularShininess = glossVal;

                if (specularPower == 0)
                {
                    glossVal = 1;
                }

                lmMaterial.SpecularColor = lmMaterial.SpecularColor * specularPower;



                if (TileTextureNormal != null)
                {
                    var tm = await MakeTextureModel(TileTextureNormal);
                    if (tm != null)
                    {
                        lmMaterial.NormalMap = tm;
                    }
                }

                // Disabled currently since this is no longer a diffuse map, and now an _orm map.
                if (TileTextureDiffuse != null && false)
                {
                    var tm = await MakeTextureModel(TileTextureDiffuse);
                    if (tm != null)
                    {
                        lmMaterial.DiffuseMap = tm;
                    }
                }

                MeshGeometryModel3D mgm3 = new MeshGeometryModel3D()
                {
                    Geometry = mg3,
                    Material = lmMaterial
                };

                Models.Add(mgm3);
            } catch(Exception ex)
            {
                FlexibleMessageBox.Show("Unable to update 3D Viewport.\n\nError: ".L() + ex.Message, "Viewport Error".L(), System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }

        private async Task<TextureModel> MakeTextureModel(XivTex tex)
        {

            var layer = (int)Math.Floor(RowData[2][3] * 64);
            if (layer > 63 || layer < 0) layer = 0;
            var tileX = (float)RowData[3][0];
            var tileY = (float)RowData[3][3];
            var tileSkewX = (float)RowData[3][1];
            var tileSkewY = (float)RowData[3][2];

            byte[] data;
            try
            {
                data = await tex.GetRawPixels(layer);
            } catch
            {
                return null;
            }

            var ogW = 32;
            var ogH = 32;
            var w = 256;
            var h = 256;
            Color4[] colors = new Color4[w * h];


            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var nPixel = (y * w) + x;
                    //U = Dot Product ([u,v], [Red, Green])
                    float u = (float)x / (float)w;
                    float v = ((float)y) / (float)h;

                    // This is now the new U and V coordinates we want to map to.
                    var newU = SharpDX.Vector2.Dot(new SharpDX.Vector2(u, v), new SharpDX.Vector2(tileX, tileSkewX)) % 1.0f;
                    var newV = SharpDX.Vector2.Dot(new SharpDX.Vector2(u, v), new SharpDX.Vector2(tileSkewY, tileY)) % 1.0f;

                    var xPx = (int)(newU * ogW);
                    var yPx = (int)(newV * ogH);

                    xPx = xPx < 0 ? ogW - xPx : xPx;
                    yPx = yPx < 0 ? ogH - yPx : yPx;

                    xPx = xPx >= ogW ? 0 : xPx;
                    yPx = yPx >= ogH ? 0 : yPx;


                    var ogPixel = (yPx * ogW) + xPx;
                    var ogDataOffset = ogPixel * 4;

                    colors[nPixel] = new Color4()
                    {
                        Red = data[ogDataOffset] / 255.0f,
                        Green = data[ogDataOffset + 1] / 255.0f,
                        Blue = data[ogDataOffset + 2] / 255.0f,
                        Alpha = 1.0f,
                    };

                }
            }

            var tm = new TextureModel(colors, w, h);
            return tm;
        }

        private enum PlaneAxis
        {
            X, Y, Z
        }
        private void AddQuad(MeshBuilder plane, PlaneAxis axis, float offset)
        {
            var uvOffset = plane.TextureCoordinates.Count;

            if (axis == PlaneAxis.X)
            {
                return;
                if (offset > 0)
                {
                    plane.AddQuad(
                        new SharpDX.Vector3(offset, -offset, offset),
                        new SharpDX.Vector3(offset, offset, offset),
                        new SharpDX.Vector3(offset, offset, -offset),
                        new SharpDX.Vector3(offset, -offset, -offset)
                    );
                }
                else
                {
                    plane.AddQuad(
                        new SharpDX.Vector3(offset, offset, offset),
                        new SharpDX.Vector3(offset, -offset, offset),
                        new SharpDX.Vector3(offset, -offset, -offset),
                        new SharpDX.Vector3(offset, offset, -offset)
                    );
                }
            }
            else if (axis == PlaneAxis.Y)
            {
                return;
                if (offset > 0)
                {
                    plane.AddQuad(
                        new SharpDX.Vector3(-offset, offset, offset),
                        new SharpDX.Vector3(offset, offset, offset),
                        new SharpDX.Vector3(offset, offset, -offset),
                        new SharpDX.Vector3(-offset, offset, -offset)
                    );
                }
                else
                {
                    plane.AddQuad(
                        new SharpDX.Vector3(offset, offset, offset),
                        new SharpDX.Vector3(-offset, offset, offset),
                        new SharpDX.Vector3(-offset, offset, -offset),
                        new SharpDX.Vector3(offset, offset, -offset)
                    );
                }
            }
            else
            {
                if (offset > 0)
                {
                    plane.AddQuad(
                        new SharpDX.Vector3(-offset, offset, offset),
                        new SharpDX.Vector3(offset, offset, offset),
                        new SharpDX.Vector3(offset, -offset, offset),
                        new SharpDX.Vector3(-offset, -offset, offset)
                    );
                }
                else
                {
                    return;
                    plane.AddQuad(
                        new SharpDX.Vector3(-offset, offset, offset),
                        new SharpDX.Vector3(offset, offset, offset),
                        new SharpDX.Vector3(offset, -offset, offset),
                        new SharpDX.Vector3(-offset, -offset, offset)
                    );
                }
            }


            plane.TextureCoordinates[uvOffset + 0] = new SharpDX.Vector2(0, 1);
            plane.TextureCoordinates[uvOffset + 1] = new SharpDX.Vector2(1, 1);
            plane.TextureCoordinates[uvOffset + 2] = new SharpDX.Vector2(1, 0);
            plane.TextureCoordinates[uvOffset + 3] = new SharpDX.Vector2(0, 0);

            plane.Normals[uvOffset + 0] = new SharpDX.Vector3(0, 0, 1);
            plane.Normals[uvOffset + 1] = new SharpDX.Vector3(0, 0, 1);
            plane.Normals[uvOffset + 2] = new SharpDX.Vector3(0, 0, 1);
            plane.Normals[uvOffset + 3] = new SharpDX.Vector3(0, 0, 1);
        }
        private MeshGeometry3D MakeCube()
        {
            var plane = new MeshBuilder();
            plane.CreateTextureCoordinates = true;

            AddQuad(plane, PlaneAxis.X, 0.5f);
            AddQuad(plane, PlaneAxis.X, -0.5f);
            AddQuad(plane, PlaneAxis.Y, 0.5f);
            AddQuad(plane, PlaneAxis.Y, -0.5f);
            AddQuad(plane, PlaneAxis.Z, 0.5f);
            AddQuad(plane, PlaneAxis.Z, -0.5f);

            plane.ComputeTangents(MeshFaces.Default);

            return plane.ToMeshGeometry3D();
        }


        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected void OnPropertyChanged([CallerMemberName]string info = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));
        }

        protected bool Set<T>(ref T backingField, T value, [CallerMemberName]string propertyName = "")
        {
            if (object.Equals(backingField, value))
            {
                return false;
            }

            backingField = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion
    }
}

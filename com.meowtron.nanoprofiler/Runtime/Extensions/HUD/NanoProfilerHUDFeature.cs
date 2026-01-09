#if UNITY_2022_3_OR_NEWER
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NanoProfiler
{
    public class NanoProfilerHUDFeature : ScriptableRendererFeature
    {
        private bool _disposed;
        private Pass _pass;
        
        public static int CharWidth => TextQuadDrawer.FONT_CHAR_WIDTH;
        public static int CharHeight => TextQuadDrawer.FONT_CHAR_SIZE;

        public override void Create()
        {
            // Don't create disposable objects in Create because
            // it's called for loaded Scriptable Object that
            // never disposed and it'll leak.
            
            _disposed = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;
            
            base.Dispose(disposing);
            _pass?.Dispose();
            _pass = null;
            _disposed = true;
        }

        public bool IsReady() => _pass != null;
        
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_disposed)
                return;
            
            _pass ??= new Pass();
            _pass.renderPassEvent = RenderPassEvent.AfterOverlay;
            
            renderer.EnqueuePass(_pass);
        }

        public void AddText(int posX, int posY, NativeText.ReadOnly text)
        {
            _pass.TextQuadDrawer.AddText(posX, posY, text);;
        }

        public void AddQuad(int posX, int posY, int sizeX, int sizeY, int colorIndex)
        {
            _pass.ChartQuadDrawer.AddQuad(posX, posY, sizeX, sizeY, colorIndex);
        }
        
        public void AddSeries(int posX, int posY, float[] values, int offset, float warningValue, float limitValue)
        {
            _pass.ChartQuadDrawer.AddSeries(posX, posY, values, offset, warningValue, limitValue);
        }
        
        private class Pass : ScriptableRenderPass, IDisposable
        {
            public readonly TextQuadDrawer TextQuadDrawer;
            public readonly ChartQuadDrawer ChartQuadDrawer;
            
            private class PassData
            {
                public TextureHandle ColorTarget;
                public Matrix4x4 ProjectionMatrix;
                public TextQuadDrawer TextQuadDrawer;
                public ChartQuadDrawer ChartQuadDrawer;
            }

            public Pass()
            {
                TextQuadDrawer = new TextQuadDrawer();
                ChartQuadDrawer = new ChartQuadDrawer();
            }

            public void Dispose()
            {
                TextQuadDrawer.Dispose();
                ChartQuadDrawer.Dispose();
            }

            private static void ExecutePass(PassData data, UnsafeGraphContext context)
            {
                data.ChartQuadDrawer.Draw(context.cmd, data.ProjectionMatrix);
                data.TextQuadDrawer.Draw(context.cmd, data.ProjectionMatrix);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.isSceneViewCamera)
                    return;
                
                using var builder = renderGraph.AddUnsafePass<PassData>("ProfilerHUD", out var passData);
                passData.ChartQuadDrawer = ChartQuadDrawer;
                passData.TextQuadDrawer = TextQuadDrawer;
                
                var resourceData = frameData.Get<UniversalResourceData>();
                passData.ColorTarget = resourceData.backBufferColor;
                
                var screenPixelSize = cameraData.camera.pixelRect;
                passData.ProjectionMatrix = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0, screenPixelSize.width, screenPixelSize.height, 0, -1.0f, 100.0f), false);
                
                builder.AllowPassCulling(false);
                builder.UseTexture(resourceData.backBufferColor, AccessFlags.Write);
                
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    context.cmd.SetRenderTarget(data.ColorTarget);
                    ExecutePass(data, context);
                });
            }
        }

        private class QuadDrawer : IDisposable
        {
            private const int VERTEX_PER_INSTANCE = 6;
            private const int INDICES_PER_INSTANCE = 6;
            
            protected record struct Quad(Vector2 Position, Vector2 Size, Vector2 UV, int colorIndex);
            
            private static readonly int ShaderBufferId = Shader.PropertyToID("_Buffer");
            private static readonly int ShaderSettingsId = Shader.PropertyToID("_Settings");
            private static readonly int ShaderProjMatrixId = Shader.PropertyToID("_ViewProjectionMatrix");
            private static readonly int ShaderMainTexId = Shader.PropertyToID("_MainTex");

            private readonly int _maxBufferSize;
            private readonly bool _supportsProceduralDraw;
            
            private readonly Material _material;
            private readonly uint[] _argsBufferValues;
            private readonly ComputeBuffer _argsBuffer;
            private readonly ComputeBuffer _dataBuffer;
            
            private readonly Mesh _legacyMesh;
            private Texture2D _texture;
            private Vector2 _textureSize;
            
            private int _lastPosition;

            protected QuadDrawer(int bufferSize)
            {
                _maxBufferSize = bufferSize;
                _supportsProceduralDraw = SystemInfo.supportsIndirectArgumentsBuffer && SystemInfo.maxComputeBufferInputsVertex > 0;

                var shader = Shader.Find("Hidden/SharedProfilerHUD");
                _material = new Material(shader);
                UpdateSettings(0, 0);
                
                if (_supportsProceduralDraw)
                {
                    _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
                    _argsBufferValues = new uint[]
                    {
                        VERTEX_PER_INSTANCE, // vertex count per instance (tri-list quad)
                        0,  // instance count
                        0, // start vertex location
                        0, // start instance location
                        0  // reserved
                    };

                    _dataBuffer = new ComputeBuffer(bufferSize, sizeof(ulong), ComputeBufferType.Structured);
                    _material.SetBuffer(ShaderBufferId, _dataBuffer);
                }
                else
                {
                    _legacyMesh = new Mesh();
                    var layout = new[]
                    {
                        new VertexAttributeDescriptor(VertexAttribute.Position),
                        new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2),
                    };
                    _legacyMesh.SetVertexBufferParams(bufferSize * VERTEX_PER_INSTANCE, layout);
                    _legacyMesh.SetIndexBufferParams(bufferSize * VERTEX_PER_INSTANCE, IndexFormat.UInt16);
                    GenerateMeshIndices();
                }
                
                _lastPosition = 0;
            }

            public void Dispose()
            {
                _argsBuffer?.Release();
                _dataBuffer?.Release();
            }

            public void AddQuad(int posX, int posY, int sizeX, int sizeY, int colorIndex)
            {
                using var quads = new NativeArray<Quad>(1, Allocator.Temp);
                var quadsSpan = quads.AsSpan();
                quadsSpan[0] = new Quad(new Vector2(posX, posY), new Vector2(sizeX, sizeY), Vector2.zero, colorIndex);
                AddQuads(quads);
            }
            
            public void Draw(UnsafeCommandBuffer cmd, Matrix4x4 projectionMatrix)
            {
                if (_lastPosition == 0)
                    return;

                if (!_material)
                    return;
                
                _material.SetMatrix(ShaderProjMatrixId, projectionMatrix);
                _material.SetTexture(ShaderMainTexId, _texture);
                
                if (_supportsProceduralDraw)
                {
                    _argsBufferValues[1] = (uint)_lastPosition; 
                    _argsBuffer.SetData(_argsBufferValues);
                
                    cmd.DrawProceduralIndirect(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, _argsBuffer);
                }
                else
                {
                    _legacyMesh.SetSubMesh(0, new SubMeshDescriptor(0, _lastPosition * INDICES_PER_INSTANCE));
                    cmd.DrawMesh(_legacyMesh, Matrix4x4.identity, _material, 0, 1);
                }
                
                _lastPosition = 0;
            }
            
            protected void SetTexture(Texture2D texture)
            {
                _texture = texture;
                _material.SetTexture(ShaderMainTexId, texture);
                UpdateSettings(texture.width, texture.height);
            }
            
            protected void AddQuads(NativeSlice<Quad> data)
            {
                if (_lastPosition + data.Length >= _maxBufferSize)
                    return;

                if (_supportsProceduralDraw)
                    GeneratePackedQuads(data);
                else
                    GenerateMeshQuads(data);
                
                _lastPosition += data.Length;
            }

            private void GeneratePackedQuads(NativeSlice<Quad> data)
            {
                using var encodedData = new NativeArray<ulong>(data.Length, Allocator.Temp);
                var encodedDataSpan = encodedData.AsSpan();

                for (int i = 0; i < data.Length; i++)
                {
                    var quad = data[i];
                    ulong encodedValue;
                    unchecked
                    {
                        // 0..11 - Position.x (1 bit longer than Position.y to support wide screens) 
                        // 12..22 - Position.y
                        // 23..33 - Size.x
                        // 34..44 - Size.y
                        // 45..52 - Tex.u
                        // 53..60 - Tex.v
                        // 61..63 - Color index
                        
                        encodedValue =
                            ((ulong)quad.Position.x & 0xFFF) << 0 |
                            ((ulong)quad.Position.y & 0x7FF) << 12 |
                            ((ulong)quad.Size.x & 0x7FF) << 23 |
                            ((ulong)quad.Size.y & 0x7FF) << 34 |
                            ((ulong)(quad.UV.x * 0xFF) & 0xFF) << 45 |
                            ((ulong)(quad.UV.y * 0xFF) & 0xFF) << 53 |
                            ((ulong)quad.colorIndex & 0x7) << 61;

                    }

                    encodedDataSpan[i] = encodedValue;
                }
                
                _dataBuffer.SetData(encodedData, 0, _lastPosition, data.Length);
            }
            
            [StructLayout(LayoutKind.Sequential)]
            record struct LegacyVertex(float3 Position, Color32 Color, half2 UV);

            private void GenerateMeshQuads(NativeSlice<Quad> data)
            {
                Vector2[] quadVertices = {
                    new(0, 1),
                    new(1, 1),
                    new(1, 0),
                    new(1, 0),
                    new(0, 0),
                    new(0, 1),
                };
                Color32[] colorTable = {
                    new Color(0.92f, 0.94f, 0.94f), // white rgb(236, 240, 241)
                    new Color(0.75f, 0.22f, 0.17f), // red rgb(192, 57, 43)
                    new Color(0.10f, 0.77f, 0.61f), // green rgb(26, 188, 156)
                    new Color(0.94f, 0.77f, 0.05f), // yellow rgb(241, 196, 15)
                    new Color(0.20f, 0.59f, 0.86f), // blue rgb(52, 152, 219)
                    new Color(0.30f, 0.82f, 0.88f), // cyan rgb(77, 208, 225)
                    new Color(0.67f, 0.27f, 0.74f), // magenta rgb(171, 71, 188)
                    new Color(0.13f, 0.13f, 0.13f, 0.4f), // black
                };
                
                using var vertices = new NativeArray<LegacyVertex>(data.Length * VERTEX_PER_INSTANCE, Allocator.Temp);
                var verticesSpan = vertices.AsSpan();
                
                for (int i = 0; i < data.Length; i++)
                {
                    var quad = data[i];
                    
                    var vertBase = i * VERTEX_PER_INSTANCE;
                    for (int vert = 0; vert < quadVertices.Length; vert++)
                    {
                        var uvShift = quadVertices[vert] * quad.Size / _textureSize;
                        var posShift = quadVertices[vert] * quad.Size;
                        var texUV = quad.UV + uvShift;
                        verticesSpan[vertBase + vert] = new LegacyVertex(
                            new Vector3(quad.Position.x + posShift.x, quad.Position.y + posShift.y, 0),
                            colorTable[quad.colorIndex],
                            new half2((half)texUV.x, (half)(1.0f - texUV.y)));
                    }
                }
                
                _legacyMesh.SetVertexBufferData(vertices, 0, _lastPosition * VERTEX_PER_INSTANCE, vertices.Length);
            }

            private void GenerateMeshIndices()
            {
                using var indices = new NativeArray<ushort>(_maxBufferSize * INDICES_PER_INSTANCE, Allocator.Temp);
                var indicesSpan = indices.AsSpan();
                
                for (int ind = 0; ind < indices.Length; ind++)
                    indicesSpan[ind] = (ushort)ind;
                
                _legacyMesh.SetIndexBufferData(indices, 0, 0, indices.Length);
            }

            private void UpdateSettings(int textureWidth, int textureHeight)
            {
                var resolution = Screen.currentResolution;
                _material.SetVector(ShaderSettingsId, new Vector4(resolution.width, resolution.height, textureWidth, textureHeight));

                _textureSize = new Vector2(textureWidth, textureHeight);
            }
        }
        
        private class TextQuadDrawer : QuadDrawer
        {
            public const int FONT_CHAR_SIZE = 32;
            public const int FONT_CHAR_WIDTH = 16;
            private const int BUFFER_SIZE = 512;

            public TextQuadDrawer() : base(BUFFER_SIZE)
            {
                SetTexture(Resources.Load<Texture2D>("SharedProfilerHUD"));
            }

            public void AddText(int posX, int posY, NativeText.ReadOnly text)
            {
                using var quads = new NativeArray<Quad>(text.Length, Allocator.Temp);
                Span<Quad> quadsSpan = quads.AsSpan();

                int quadsCount = 0;
                int colorCode = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    char sym = (char)text[i];
                    if (sym == '^' && ((i + 1) < text.Length))
                    {
                        int newColorCode = text[i + 1] - '0';
                        if (newColorCode is >= 0 and <= 7)
                        {
                            colorCode = newColorCode;
                            i++;
                            continue;
                        }
                    }

                    var uv = new Vector2((float)(sym & 0xF) / 0x10, (float)(sym >> 4) / 0x10);
                    quadsSpan[quadsCount] = new Quad(new Vector2(posX, posY), new Vector2(FONT_CHAR_SIZE, FONT_CHAR_SIZE), uv, colorCode);
                    
                    quadsCount++;
                    posX += FONT_CHAR_WIDTH;
                }

                AddQuads(quads.Slice(0, quadsCount));
            }
        }
        
        private class ChartQuadDrawer : QuadDrawer
        {
            private const int CHART_WIDTH = 4;
            private const int CHART_SCALE = 16;
            private const int BUFFER_SIZE = 4096;

            public ChartQuadDrawer() : base(BUFFER_SIZE)
            {
                SetTexture(Texture2D.whiteTexture);
            }

            public void AddSeries(int posX, int posY, float[] values, int offset, float warningValue, float limitValue)
            {
                using var quads = new NativeArray<Quad>(values.Length, Allocator.Temp);
                Span<Quad> quadsSpan = quads.AsSpan();

                float maxValue = warningValue;
                foreach (float val in values)
                    maxValue = Math.Max(val, maxValue);

                // Draw samples in reverse order, so that newest one is to the left
                int valuesIndex = offset;
                for (int i = 0; i < values.Length; i++)
                {
                    if (valuesIndex < 0)
                        valuesIndex = values.Length - 1;

                    float value = values[valuesIndex];
                    
                    int colorIndex;
                    if (value > limitValue)
                        colorIndex = 1; // red
                    else if (value > warningValue)
                        colorIndex = 3; // yellow
                    else
                        colorIndex = 4; // blue
                    
                    var val = values[valuesIndex] * CHART_SCALE / maxValue;
                    quadsSpan[i] = new Quad(new Vector2(posX, posY + CHART_SCALE - val), new Vector2(CHART_WIDTH, val), Vector2.zero, colorIndex);
                    
                    posX += CHART_WIDTH + 1;
                    valuesIndex--;
                }
                
                AddQuads(quads);
            }
        }
    }    
}
#endif
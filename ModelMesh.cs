using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.WIC;
using System;
using System.IO;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace Undistort
{
    internal class ModelMesh
    {
        private InputElement[] m_inputElements;
        public InputElement[] InputElements
        {
            set { m_inputElements = value; }
            get { return m_inputElements; }
        }

        private InputLayout m_inputLayout;
        public InputLayout InputLayout
        {
            set { m_inputLayout = value; }
            get { return m_inputLayout; }
        }

        private int m_vertexSize;
        public int VertexSize
        {
            set { m_vertexSize = value; }
            get { return m_vertexSize; }
        }

        private Buffer m_vertexBuffer;
        public Buffer VertexBuffer
        {
            set { m_vertexBuffer = value; m_vertexBufferBinding = new VertexBufferBinding(m_vertexBuffer, m_vertexSize, 0); }
            get { return m_vertexBuffer; }
        }

        private VertexBufferBinding m_vertexBufferBinding;
        public VertexBufferBinding VertexBufferBinding { get => m_vertexBufferBinding; }

        private Buffer m_indexBuffer;
        public Buffer IndexBuffer
        {
            set { m_indexBuffer = value; }
            get { return m_indexBuffer; }
        }

        private int m_vertexCount;
        public int VertexCount
        {
            set { m_vertexCount = value; }
            get { return m_vertexCount; }
        }

        private int m_indexCount;
        public int IndexCount
        {
            set { m_indexCount = value; }
            get { return m_indexCount; }
        }

        private int m_primitiveCount;
        public int PrimitiveCount
        {
            set { m_primitiveCount = value; }
            get { return m_primitiveCount; }
        }

        private PrimitiveTopology m_primitiveTopology;
        public PrimitiveTopology PrimitiveTopology
        {
            set { m_primitiveTopology = value; }
            get { return m_primitiveTopology; }
        }

        private Texture2D m_diffuseTexture;
        public Texture2D DiffuseTexture
        {
            set { m_diffuseTexture = value; }
            get { return m_diffuseTexture; }
        }

        private ShaderResourceView m_diffuseTextureView;
        public ShaderResourceView DiffuseTextureView
        {
            set { m_diffuseTextureView = value; }
            get { return m_diffuseTextureView; }
        }

        //add texture and texture view for the shader
        public void AddTextureDiffuse(Device device, string path)
        {
            using (var factory2 = new ImagingFactory2())
            {
                m_diffuseTexture = LoadFromFile(device, factory2, path);
                m_diffuseTextureView = new ShaderResourceView(device, m_diffuseTexture);
            }
        }

        //set the input layout and make sure it matches vertex format from the shader
        public void SetInputLayout(Device device, ShaderSignature inputSignature)
        {
            m_inputLayout = new InputLayout(device, inputSignature, m_inputElements);
            if (m_inputLayout == null)
            {
                throw new Exception("mesh and vertex shader input layouts do not match!");
            }
        }

        //dispose D3D related resources
        public void Dispose()
        {
            m_inputLayout.Dispose();
            m_vertexBuffer.Dispose();
            m_indexBuffer.Dispose();
        }

        private BitmapSource LoadBitmap(ImagingFactory2 factory, string filename)
        {
            if (!File.Exists(filename))
                throw new Exception("File " + filename + " doesn't exist.");

            var bitmapDecoder = new BitmapDecoder(
                factory,
                filename,
                DecodeOptions.CacheOnDemand
                );

            var result = new FormatConverter(factory);

            result.Initialize(
                bitmapDecoder.GetFrame(0),
                PixelFormat.Format32bppPRGBA,
                BitmapDitherType.None,
                null,
                0.0,
                BitmapPaletteType.Custom);

            return result;
        }

        private Texture2D CreateTexture2DFromBitmap(Device device, BitmapSource bitmapSource)
        {
            // Allocate DataStream to receive the WIC image pixels
            int stride = bitmapSource.Size.Width * 4;
            using (var buffer = new SharpDX.DataStream(bitmapSource.Size.Height * stride, true, true))
            {
                // Copy the content of the WIC to the buffer
                bitmapSource.CopyPixels(stride, buffer);
                return new Texture2D(device, new SharpDX.Direct3D11.Texture2DDescription()
                {
                    Width = bitmapSource.Size.Width,
                    Height = bitmapSource.Size.Height,
                    ArraySize = 1,
                    BindFlags = BindFlags.ShaderResource,
                    Usage = ResourceUsage.Immutable,
                    CpuAccessFlags = CpuAccessFlags.None,
                    Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                    MipLevels = 1,
                    OptionFlags = ResourceOptionFlags.None,
                    SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                }, new SharpDX.DataRectangle(buffer.DataPointer, stride));
            }
        }

        private Texture2D LoadFromFile(Device device, ImagingFactory2 factory, string fileName)
        {
            if (!File.Exists(fileName))
                throw new Exception("File " + fileName + " doesn't exist.");

            Texture2D texture = null;
            switch (Path.GetExtension(fileName).ToLowerInvariant())
            { 
            case ".tga":
                {
                    var scratch = DirectXTexNet.TexHelper.Instance.LoadFromTGAFile(fileName);
                    texture = new Texture2D(scratch.CreateTexture(device.NativePointer));
                    break;
                }
            default:
                {
                    var bs = LoadBitmap(factory, fileName);
                    texture = CreateTexture2DFromBitmap(device, bs);
                    break;
                }
            }
            return texture;
        }
    }
}

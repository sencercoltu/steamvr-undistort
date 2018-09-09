using System;
using System.Collections.Generic;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.D3DCompiler;
using Device = SharpDX.Direct3D11.Device;

namespace Undistort
{
    // A container for the meshes loaded from the file
    class Model
    {
        List<ModelMesh> m_meshes;
        bool m_inputLayoutSet;

        Vector3 m_aaBoxMin;
        public Vector3 AABoxMin
        {
            set { m_aaBoxMin = value; }
            get { return m_aaBoxMin; }
        }

        Vector3 m_aaBoxMax;
        public Vector3 AABoxMax
        {
            set { m_aaBoxMax = value; }
            get { return m_aaBoxMax; }
        }

        Vector3 m_aaBoxCentre;
        public Vector3 AABoxCentre
        {
            set { m_aaBoxCentre = value; }
            get { return m_aaBoxCentre; }
        }

        public Model()
        {
            m_meshes = new List<ModelMesh>();
            m_inputLayoutSet = false;
        }

        public void AddMesh(ref ModelMesh mesh)
        {
            m_meshes.Add(mesh);
        }

        public void SetAABox(Vector3 min, Vector3 max)
        {
            m_aaBoxMin = min;
            m_aaBoxMax = max;
            m_aaBoxCentre = 0.5f * (min + max);
        }

        //Go through the meshes and render them
        public void Render(DeviceContext context)
        {
            if (!m_inputLayoutSet)
                throw new Exception("Model::Render(): input layout has not be specified, you must call SetInputLayout() before calling Render()");

            foreach (ModelMesh mesh in m_meshes)
            {
                //set mesh specific data
                context.InputAssembler.InputLayout = mesh.InputLayout;
                context.InputAssembler.PrimitiveTopology = mesh.PrimitiveTopology;
                context.InputAssembler.SetVertexBuffers(0, mesh.VertexBufferBinding);
                context.InputAssembler.SetIndexBuffer(mesh.IndexBuffer, Format.R32_UInt, 0);
                context.PixelShader.SetShaderResource(0, mesh.DiffuseTextureView);

                //draw
                context.DrawIndexed(mesh.IndexCount, 0, 0);
            }
        }

        public void SetInputLayout( Device device, ShaderSignature inputSignature )
        {
            foreach (ModelMesh mesh in m_meshes)
            {
                mesh.SetInputLayout(device, inputSignature);
            }
            m_inputLayoutSet = true;
        }

        public void Dispose()
        {
            foreach (ModelMesh mesh in m_meshes)
            {
                mesh.Dispose();
            }
        }

    }
}

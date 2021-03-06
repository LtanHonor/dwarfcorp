using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;

namespace DwarfCorp
{
    [JsonObject(IsReference =true)]
    public class SimpleSprite : Tinter, IRenderableComponent
    {
        public enum OrientMode
        {
            Fixed,
            Spherical,
            YAxis,
        }

        public OrientMode OrientationType = OrientMode.Spherical;
        public bool DrawSilhouette = false;
        public Color SilhouetteColor = new Color(0.0f, 1.0f, 1.0f, 0.5f);
        public bool EnableWind = false;
        public float WorldWidth = 1.0f;
        public float WorldHeight = 1.0f;
        private Vector3 prevDistortion = Vector3.Zero;
        [JsonProperty]
        private SpriteSheet Sheet;
        [JsonProperty]
        private Point Frame;
        private ExtendedVertex[] Verticies;
        private int[] Indicies;

        public SimpleSprite(
            ComponentManager Manager,
            String Name,
            Matrix LocalTransform,
            bool AddToCollisionManager,
            SpriteSheet Sheet,
            Point Frame) 
            : base(Manager, Name, LocalTransform, Vector3.Zero, Vector3.Zero, AddToCollisionManager)
        {
            this.Sheet = Sheet;
            this.Frame = Frame;
       }

        public SimpleSprite()
        {
        }
        
        public void SetFrame(Point Frame)
        {
            this.Frame = Frame;
            Verticies = null;
        }

        // Perhaps should be handled in base class?
        public override void ReceiveMessageRecursive(Message messageToReceive)
        {
            switch(messageToReceive.Type)
            {
                case Message.MessageType.OnChunkModified:
                    HasMoved = true;
                    break;
            }


            base.ReceiveMessageRecursive(messageToReceive);
        }

        public override void RenderSelectionBuffer(DwarfTime gameTime, ChunkManager chunks, Camera camera, SpriteBatch spriteBatch,
            GraphicsDevice graphicsDevice, Shader effect)
        {
            if (!IsVisible) return;

            base.RenderSelectionBuffer(gameTime, chunks, camera, spriteBatch, graphicsDevice, effect);
            effect.SelectionBufferColor = this.GetGlobalIDColor().ToVector4();
            Render(gameTime, chunks, camera, spriteBatch, graphicsDevice, effect, false);
        }

        public void AutoSetWorldSize()
        {
            if (Sheet == null)
                return;
            WorldWidth = Sheet.FrameWidth / 32.0f;
            WorldHeight = Sheet.FrameHeight / 32.0f;
        }

        new public void Render(DwarfTime gameTime,
            ChunkManager chunks,
            Camera camera,
            SpriteBatch spriteBatch,
            GraphicsDevice graphicsDevice,
            Shader effect,
            bool renderingForWater)
        {
            base.Render(gameTime, chunks, camera, spriteBatch, graphicsDevice, effect, renderingForWater);

            if (!IsVisible)
                return;

            if (Sheet == null)
                return;

            if (Verticies == null)
            {
                System.Diagnostics.Debug.Assert(Sheet != null);

                float normalizeX = Sheet.FrameWidth / (float)(Sheet.Width);
                float normalizeY = Sheet.FrameHeight / (float)(Sheet.Height);

                List<Vector2> uvs = new List<Vector2>
                {
                    new Vector2(1.0f, 0.0f),
                    new Vector2(0.0f, 0.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(1.0f, 1.0f)
                };

                Vector2 pixelCoords = new Vector2(Frame.X * Sheet.FrameWidth, Frame.Y * Sheet.FrameHeight);
                Vector2 normalizedCoords = new Vector2(pixelCoords.X / (float)Sheet.Width, pixelCoords.Y / (float)Sheet.Height);
                var bounds = new Vector4(normalizedCoords.X + 0.001f, normalizedCoords.Y + 0.001f, normalizedCoords.X + normalizeX - 0.001f, normalizedCoords.Y + normalizeY - 0.001f);

                for (int vert = 0; vert < 4; vert++)
                {
                    uvs[vert] = new Vector2(normalizedCoords.X + uvs[vert].X * normalizeX, normalizedCoords.Y + uvs[vert].Y * normalizeY);
                }
                
                Vector3 topLeftFront = new Vector3(-0.5f * WorldWidth, 0.5f * WorldHeight, 0.0f);
                Vector3 topRightFront = new Vector3(0.5f * WorldWidth, 0.5f * WorldHeight, 0.0f);
                Vector3 btmRightFront = new Vector3(0.5f * WorldWidth, -0.5f * WorldHeight, 0.0f);
                Vector3 btmLeftFront = new Vector3(-0.5f * WorldWidth, -0.5f * WorldHeight, 0.0f);

                Verticies = new[]
                {
                    new ExtendedVertex(topLeftFront, Color.White, Color.White, uvs[0], bounds), // 0
                    new ExtendedVertex(topRightFront, Color.White, Color.White, uvs[1], bounds), // 1
                    new ExtendedVertex(btmRightFront, Color.White, Color.White, uvs[2], bounds), // 2
                    new ExtendedVertex(btmLeftFront, Color.White, Color.White, uvs[3], bounds) // 3
                };

                Indicies = new int[]
                {
                    0, 1, 3,
                    1, 2, 3
                };
            }

            // Everything that draws should set it's tint, making this pointless.
            Color origTint = effect.VertexColorTint;  
            ApplyTintingToEffect(effect);            

            var currDistortion = VertexNoise.GetNoiseVectorFromRepeatingTexture(GlobalTransform.Translation);
            var distortion = currDistortion * 0.1f + prevDistortion * 0.9f;
            prevDistortion = distortion;
            switch (OrientationType)
            {
                case OrientMode.Spherical:
                    {
                        Matrix bill = Matrix.CreateBillboard(GlobalTransform.Translation, camera.Position, camera.UpVector, null) * Matrix.CreateTranslation(distortion);
                        effect.World = bill;
                        break;
                    }
                case OrientMode.Fixed:
                    {
                        Matrix rotation = GlobalTransform;
                        rotation.Translation = rotation.Translation + distortion;
                        effect.World = rotation;
                        break;
                    }
                case OrientMode.YAxis:
                    {
                        Matrix worldRot = Matrix.CreateConstrainedBillboard(GlobalTransform.Translation, camera.Position, Vector3.UnitY, null, null);
                        worldRot.Translation = worldRot.Translation + distortion;
                        effect.World = worldRot;
                        break;
                    }
            }

            effect.MainTexture = Sheet.GetTexture();

            if (DrawSilhouette)
            {
                Color oldTint = effect.VertexColorTint; 
                effect.VertexColorTint = SilhouetteColor;
                graphicsDevice.DepthStencilState = DepthStencilState.None;
                var oldTechnique = effect.CurrentTechnique;
                effect.CurrentTechnique = effect.Techniques[Shader.Technique.Silhouette];
                foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    graphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList,
                        Verticies, 0, 4, Indicies, 0, 2);
                }

                graphicsDevice.DepthStencilState = DepthStencilState.Default;
                effect.VertexColorTint = oldTint;
                effect.CurrentTechnique = oldTechnique;
            }

                effect.EnableWind = EnableWind;

            foreach(EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList,
                                        Verticies, 0, 4, Indicies, 0, 2);
            }

            effect.VertexColorTint = origTint;
            effect.EnableWind = false;
            EndDraw(effect);
        }
    }

}

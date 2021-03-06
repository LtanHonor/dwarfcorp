using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using System.Threading;
using Newtonsoft.Json;

namespace DwarfCorp
{
    /// <summary>
    /// Handles components. All game objects (dwarves, trees, lamps, ravenous wolverines) are just a 
    /// collection of components. Together, the collection is called an 'entity'. Components form a 
    /// tree. Each component has a parent and 0 to N children.
    /// </summary>
    public class ComponentManager
    {
        //[Saving.SaveableObject(0)]
        public class ComponentSaveData //: Saving.ISaveableObject
        {
            /*public class ComponentSaveNugget : Saving.Nugget
            {
                public List<Saving.Nugget> SaveableComponents;
                public uint RootComponent;
            }*/

            public List<GameComponent> SaveableComponents;
            public uint RootComponent;

            /*Saving.Nugget Saving.ISaveableObject.SaveToNugget(Saving.Saver SaveSystem)
            {
                return new ComponentSaveNugget
                {
                    SaveableComponents = SaveableComponents.Select(o => SaveSystem.SaveObject(o)).ToList(),
                    RootComponent = RootComponent
                };
            }

            void Saving.ISaveableObject.LoadFromNugget(Saving.Loader SaveSystem, Saving.Nugget From)
            {
                var n = From as ComponentSaveNugget;
                SaveableComponents = n.SaveableComponents.Select(o => SaveSystem.LoadObject(o) as GameComponent).ToList();
                RootComponent = n.RootComponent;
            }*/
        }

        private Dictionary<uint, GameComponent> Components;
        private uint MaxGlobalID = 0;
        public const int InvalidID = 0;
        //private Dictionary<System.Type, List<IUpdateableComponent>> UpdateableComponents =
        //    new Dictionary<Type, List<IUpdateableComponent>>();
        private List<IUpdateableComponent> UpdateableComponents = new List<IUpdateableComponent>();
        private List<IRenderableComponent> Renderables = new List<IRenderableComponent>();
        private List<MinimapIcon> MinimapIcons = new List<MinimapIcon>();
        private List<GameComponent> Removals = new List<GameComponent>();
        private List<GameComponent> Additions = new List<GameComponent>();

        public List<IRenderableComponent> GetRenderables()
        {
            return Renderables;
        }

        public Body RootComponent { get; private set; }

        public void SetRootComponent(Body Component)
        {
            RootComponent = Component;
        }

        private Mutex AdditionMutex = new Mutex();
        private Mutex RemovalMutex = new Mutex();

        public IEnumerable<MinimapIcon> GetMinimapIcons() { return MinimapIcons; }

        public WorldManager World { get; set; }

        public ComponentSaveData GetSaveData()
        {
            // Just in case the root was tagged unserializable for whatever reason.
            RootComponent.SetFlag(GameComponent.Flag.ShouldSerialize, true);

            foreach (var component in Components)
                component.Value.PrepareForSerialization();

            var serializableComponents = Components.Where(c => c.Value.IsFlagSet(GameComponent.Flag.ShouldSerialize)).Select(c => c.Value).ToList();

            return new ComponentSaveData
            {
                SaveableComponents = serializableComponents,
                RootComponent = RootComponent.GlobalID
            };
        }

        /// <summary>
        /// Must be called after serialization to avoid leaking references to dead components.
        /// </summary>
        public void CleanupSaveData()
        {
            foreach (var component in Components)
                component.Value.SerializableChildren = null;
        }

        public ComponentManager(ComponentSaveData SaveData, WorldManager World)
        {
            this.World = World;
            World.ComponentManager = this;
            Components = new Dictionary<uint, GameComponent>();
            SaveData.SaveableComponents.RemoveAll(c => c == null);
            foreach (var component in SaveData.SaveableComponents)
            {
                Components.Add(component.GlobalID, component);
                component.World = World;
            }
            RootComponent = Components[SaveData.RootComponent] as Body;

            foreach (var component in Components)
            {
                if (component.Value is IUpdateableComponent)
                {
                    //var type = component.Value.GetType();
                    //if (!UpdateableComponents.ContainsKey(type))
                    //    UpdateableComponents.Add(type, new List<IUpdateableComponent>());
                    //UpdateableComponents[type].Add(component.Value as IUpdateableComponent);
                    UpdateableComponents.Add(component.Value as IUpdateableComponent);
                }

                if (component.Value is IRenderableComponent)
                    Renderables.Add(component.Value as IRenderableComponent);

                if (component.Value is MinimapIcon)
                    MinimapIcons.Add(component.Value as MinimapIcon);
            }

            MaxGlobalID = Components.Aggregate<KeyValuePair<uint, GameComponent>, uint>(0, (current, component) => Math.Max(current, component.Value.GlobalID));

            foreach (var component in SaveData.SaveableComponents)
                component.PostSerialization(this);

            foreach (var component in SaveData.SaveableComponents)
            {
                component.CreateCosmeticChildren(this);
            }

            var removals = SaveData.SaveableComponents.Where(p => p.Parent == null && p != RootComponent).ToList();

            foreach(var component in removals)
            {
                Console.Error.WriteLine("Component {0} has no parent. removing.", component.Name);
                RemoveComponentImmediate(component);
                SaveData.SaveableComponents.Remove(component);
            }

           

            /*
            foreach (var component in Components)
            {
                if (component.Value.Parent != null && (!HasComponent(component.Value.Parent.GlobalID) || !component.Value.Parent.Children.Contains(component.Value)))
                {
                    Console.Error.WriteLine("Component {0} parent: {1} is not in the list of components", component.Value.Name, component.Value.Parent.Name);
                }
            */
        }

        public ComponentManager(WorldManager state)
        {
            World = state;
            Components = new Dictionary<uint, GameComponent>();
        }

        public List<Body> SelectRootBodiesOnScreen(Rectangle selectionRectangle, Camera camera)
        {
            if (World.SelectionBuffer == null)
                return new List<Body>();

            HashSet<Body> toReturn = new HashSet<Body>(); // Hashset ensures all bodies are unique.
            foreach (uint id in World.SelectionBuffer.GetIDsSelected(selectionRectangle))
            {
                GameComponent component;
                if (!Components.TryGetValue(id, out component))
                    continue;

                if (!component.IsVisible) continue; // Then why was it drawn in the selection buffer??
                var toAdd = component.GetRoot().GetComponent<Body>();
                if (!toReturn.Contains(toAdd))
                    toReturn.Add(toAdd);
            }
            return toReturn.ToList();
        }

        public void AddComponent(GameComponent component)
        {
            AdditionMutex.WaitOne();

            MaxGlobalID += 1;
            component.GlobalID = MaxGlobalID;
            Additions.Add(component);

            AdditionMutex.ReleaseMutex();
        }

        public void RemoveComponent(GameComponent component)
        {
            RemovalMutex.WaitOne();
            Removals.Add(component);
            RemovalMutex.ReleaseMutex();
        }

        public bool HasComponent(uint id)
        {
            return Components.ContainsKey(id);
        }

        private void RemoveComponentImmediate(GameComponent component)
        {
            if (!Components.ContainsKey(component.GlobalID))
                return;

            Components.Remove(component.GlobalID);

            if (component is IUpdateableComponent)
            {
                //var type = component.GetType();
                //if (UpdateableComponents.ContainsKey(type))
                //    UpdateableComponents[type].Remove(component as IUpdateableComponent);
                UpdateableComponents.Remove(component as IUpdateableComponent);
            }

            if (component is IRenderableComponent)
                Renderables.Remove(component as IRenderableComponent);

            if (component is MinimapIcon)
                MinimapIcons.Remove(component as MinimapIcon);

            foreach (var child in new List<GameComponent>(component.EnumerateChildren()))
                RemoveComponentImmediate(child);
        }

        private void AddComponentImmediate(GameComponent component)
        {
            if (Components.ContainsKey(component.GlobalID))
            {
                if (Object.ReferenceEquals(Components[component.GlobalID], component)) return;
                throw new InvalidOperationException("Attempted to add component with same ID as existing component.");
            }

            Components[component.GlobalID] = component;

            if (component is IUpdateableComponent)
            {
                //var type = component.GetType();
                //if (!UpdateableComponents.ContainsKey(type))
                //    UpdateableComponents.Add(type, new List<IUpdateableComponent>());
                //UpdateableComponents[type].Add(component as IUpdateableComponent);
                UpdateableComponents.Add(component as IUpdateableComponent);
            }

            if (component is IRenderableComponent)
                Renderables.Add(component as IRenderableComponent);

            if (component is MinimapIcon)
                MinimapIcons.Add(component as MinimapIcon);
        }

        public void Update(DwarfTime gameTime, ChunkManager chunks, Camera camera)
        {
            PerformanceMonitor.PushFrame("Component Update");

            foreach (var component in UpdateableComponents)
                component.Update(gameTime, chunks, camera);

            PerformanceMonitor.PopFrame();

            AddRemove();
        }

        private void AddRemove()
        {
            AdditionMutex.WaitOne();
            foreach (GameComponent component in Additions)
                AddComponentImmediate(component);

            Additions.Clear();
            AdditionMutex.ReleaseMutex();

            RemovalMutex.WaitOne();
            foreach (GameComponent component in Removals)
                RemoveComponentImmediate(component);

            Removals.Clear();
            RemovalMutex.ReleaseMutex();
        }

        public void UpdatePaused()
        {
            AddRemove();
        }

        public GameComponent FindComponent(uint ID)
        {
            GameComponent result = null;
            Components.TryGetValue(ID, out result);
            return result;
        }
    }
}

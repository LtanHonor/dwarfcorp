﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DwarfCorp
{
    /// <summary> Causes the creature to have a Thought for a specified time </summary>
    public class ThoughtBuff : Buff
    {
        public ThoughtBuff()
        {
        }

        public ThoughtBuff(float time, Thought.ThoughtType type) :
            base(time)
        {
            ThoughtType = type;
        }

        /// <summary> The Thought the creature has during the buff </summary>
        public Thought.ThoughtType ThoughtType { get; set; }

        public override void OnApply(Creature creature)
        {
            creature.AI.AddThought(ThoughtType);
            base.OnApply(creature);
        }

        public override void OnEnd(Creature creature)
        {
            creature.AI.RemoveThought(ThoughtType);
            base.OnApply(creature);
        }

        public override Buff Clone()
        {
            return new ThoughtBuff
            {
                EffectTime = new Timer(EffectTime.TargetTimeSeconds, EffectTime.TriggerOnce, EffectTime.Mode),
                Particles = Particles,
                ParticleTimer =
                    new Timer(ParticleTimer.TargetTimeSeconds, ParticleTimer.TriggerOnce, ParticleTimer.Mode),
                SoundOnEnd = SoundOnEnd,
                SoundOnStart = SoundOnStart,
                ThoughtType = ThoughtType
            };
        }
    }
}

// Copyright (c) Shane Woolcock. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Textures;
using osu.Framework.Utils;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Rush.Objects;
using osu.Game.Rulesets.Rush.Objects.Drawables;
using osuTK;

namespace osu.Game.Rulesets.Rush.UI
{
    public class RushPlayerSprite : CompositeDrawable
    {
        private const float jump_duration = 150f;
        private const float fall_delay = 300f;
        private const float fall_duration = 150f;
        private const float travel_duration = 150f;
        private const float run_reset_delay = 200f;

        private readonly Dictionary<PlayerAnimation, TextureAnimation> textureAnimations = new Dictionary<PlayerAnimation, TextureAnimation>();

        private double runResetTime;

        private PlayerTargetLane target;

        public PlayerTargetLane Target
        {
            get => target;
            set
            {
                if (value == PlayerTargetLane.MiniBoss)
                    playAnimation(PlayerAnimation.AirAttack);

                if (value == target)
                    return;

                target = value;

                switch (value)
                {
                    case PlayerTargetLane.None:
                        fall();
                        break;

                    case PlayerTargetLane.HoldAir:
                        easeToAir();
                        playAnimation(PlayerAnimation.Hold);
                        break;

                    case PlayerTargetLane.AttackAir:
                        easeToAir();
                        playAnimation(PlayerAnimation.AirAttack);
                        break;

                    case PlayerTargetLane.HoldGround:
                        easeToGround();
                        playAnimation(PlayerAnimation.Hold);
                        break;

                    case PlayerTargetLane.AttackGround:
                        easeToGround();
                        playAnimation(PlayerAnimation.GroundAttack);
                        break;

                    case PlayerTargetLane.HoldBoth:
                        easeToCentre();
                        playAnimation(PlayerAnimation.Hold);
                        break;

                    case PlayerTargetLane.AttackBoth:
                    case PlayerTargetLane.MiniBoss:
                        easeToCentre();
                        playAnimation(PlayerAnimation.AirAttack);
                        break;

                    case PlayerTargetLane.GhostAir:
                        easeToGround();
                        // showGhost(LanedHitLane.Air);
                        break;

                    case PlayerTargetLane.GhostGround:
                        easeToAir();
                        // showGhost(LanedHitLane.Ground);
                        break;
                }

                Target = value & ~PlayerTargetLane.AttackBoth;
            }
        }

        private readonly float groundY;
        private readonly float airY;

        public RushPlayerSprite(float groundY, float airY)
        {
            this.groundY = groundY;
            this.airY = airY;

            AddRangeInternal(Enum.GetValues(typeof(PlayerAnimation)).Cast<PlayerAnimation>().Select(createTextureAnimation));
        }

        private TextureAnimation createTextureAnimation(PlayerAnimation animation) =>
            textureAnimations[animation] = new TextureAnimation
            {
                Origin = Anchor.Centre,
                Anchor = Anchor.Centre,
                DefaultFrameLength = 1000f / 16f,
                Loop = false,
                Scale = new Vector2(2),
                Alpha = 0,
            };

        [BackgroundDependencyLoader]
        private void load(TextureStore store)
        {
            textureAnimations[PlayerAnimation.Run].AddFrames(Enumerable.Range(0, 10).Select(i => store.Get($"Player/Run__{i:D3}")));
            textureAnimations[PlayerAnimation.Jump].AddFrames(Enumerable.Range(0, 10).Select(i => store.Get($"Player/Jump__{i:D3}")));
            textureAnimations[PlayerAnimation.GroundAttack].AddFrames(Enumerable.Range(0, 10).Select(i => store.Get($"Player/Attack__{i:D3}")));
            textureAnimations[PlayerAnimation.AirAttack].AddFrames(Enumerable.Range(0, 10).Select(i => store.Get($"Player/Jump_Attack__{i:D3}")));
            textureAnimations[PlayerAnimation.Hold].AddFrames(Enumerable.Range(0, 10).Select(i => store.Get($"Player/Slide__{i:D3}")));
            textureAnimations[PlayerAnimation.Hurt].AddFrame(store.Get("Player/Dead__000"));

            textureAnimations[PlayerAnimation.Run].Loop = true;
            textureAnimations[PlayerAnimation.Hold].Loop = true;

            playAnimation(PlayerAnimation.Run);
        }

        public void StopAll() => InternalChildren.OfType<TextureAnimation>().ForEach(a =>
        {
            a.Stop();
            a.Hide();
        });

        // private void playRunning()
        // {
        //     StopAll();
        //     textureAnimations[PlayerAnimation.Run].Show();
        //     textureAnimations[PlayerAnimation.Run].Restart();
        // }
        //
        // private void playJumping()
        // {
        //     StopAll();
        //
        //     runResetTime = Time.Current + run_reset_delay;
        //
        //     textureAnimations[PlayerAnimation.Jump].Show();
        //     textureAnimations[PlayerAnimation.Jump].Restart();
        // }
        //
        // private void playHold()
        // {
        //     StopAll();
        //     textureAnimations[PlayerAnimation.Hold].Show();
        //     textureAnimations[PlayerAnimation.Hold].Restart();
        // }
        //
        // private void playAttack(LanedHitLane lane)
        // {
        //     StopAll();
        //
        //     runResetTime = Time.Current + run_reset_delay;
        //
        //     var animation = lane == LanedHitLane.Air ? PlayerAnimation.AirAttack : PlayerAnimation.GroundAttack;
        //     textureAnimations[animation].Show();
        //     textureAnimations[animation].Restart();
        // }

        private void playAnimation(PlayerAnimation animation, bool delayNextRunAnimation = true)
        {
            StopAll();

            if (delayNextRunAnimation && animation != PlayerAnimation.Run)
                runResetTime = Time.Current + run_reset_delay;

            textureAnimations[animation].Show();
            textureAnimations[animation].Restart();
        }

        /// <summary>
        /// Handles any leftover actions that were not consumed by hitobjects.
        /// Allows the player to jump over sawblades or punch the ground.
        /// </summary>
        public bool HandleAction(RushAction action)
        {
            if (Target != PlayerTargetLane.None)
                return false;

            var eq = Precision.AlmostEquals(Y, groundY);

            if ((action == RushAction.AirPrimary || action == RushAction.AirSecondary) && eq)
                jump();
            else if ((action == RushAction.GroundPrimary || action == RushAction.GroundSecondary) && !eq)
                Target |= PlayerTargetLane.AttackGround;

            return true;
        }

        private void jump()
        {
            ClearTransforms();
            playAnimation(PlayerAnimation.Jump);
            this.MoveToY(airY, jump_duration, Easing.Out)
                .OnComplete(_ => fall());
        }

        private void fall(bool immediately = false)
        {
            using (BeginDelayedSequence(immediately ? 0 : fall_delay))
            {
                this.MoveToY(groundY, fall_duration, Easing.In)
                    .OnComplete(_ => playAnimation(PlayerAnimation.Run));
            }
        }

        private void easeTo(float y)
        {
            ClearTransforms();

            if (Precision.AlmostEquals(Y, y))
                Y = y;
            else
                this.MoveToY(y, travel_duration, Easing.OutQuint);
        }

        private void easeToCentre() => easeTo((airY + groundY) / 2f);

        private void easeToAir() => easeTo(airY);

        private void easeToGround() => easeTo(groundY);

        public void HandleResult(DrawableRushHitObject judgedObject, JudgementResult result)
        {
            switch (judgedObject.HitObject)
            {
                case NoteSheetHead head:
                    Target = Target.WithHoldLane(head.Lane, result.IsHit);
                    break;

                case NoteSheetTail _:
                case NoteSheetBody _:
                    if (judgedObject.HitObject is NoteSheetBody && result.IsHit)
                        break;

                    var lanedHit = (LanedHit)judgedObject.HitObject;

                    Target = Target.WithHoldLane(lanedHit.Lane, false);

                    // special case, need to ensure that we always drop to the ground if there are no holds
                    if ((Target & PlayerTargetLane.HoldBoth) == 0)
                        easeToGround();

                    break;

                case Minion minion when result.IsHit:
                    Target = Target.WithAttackLane(minion.Lane, true);
                    break;

                case DualOrb dualOrb:
                    DrawableDualOrb ddo = (DrawableDualOrb)judgedObject;
                    Target = Target.WithAttackLane(dualOrb.Air.Lane, ddo.Air.Result.IsHit).WithAttackLane(dualOrb.Ground.Lane, ddo.Ground.Result.IsHit);
                    break;

                case MiniBoss _:
                    Target = PlayerTargetLane.None;
                    fall(true);
                    break;

                case Heart heart when result.IsHit:
                    Target = Target.WithAttackLane(heart.Lane, true);
                    break;
            }
        }

        public bool CollidesWith(HitObject hitObject)
        {
            const float damage_range = 25f;
            const float heart_range = 50f;

            switch (hitObject)
            {
                case MiniBoss _:
                    return true;

                case Heart heart:
                    return Math.Abs(Y - (heart.Lane == LanedHitLane.Air ? airY : groundY)) <= heart_range;

                case LanedHit lanedHit:
                    return Math.Abs(Y - (lanedHit.Lane == LanedHitLane.Air ? airY : groundY)) <= damage_range;
            }

            return false;
        }

        protected override void Update()
        {
            base.Update();

            if (Target == PlayerTargetLane.None && !textureAnimations[PlayerAnimation.Run].IsPlaying && runResetTime <= Time.Current && Precision.AlmostEquals(Y, groundY))
                playAnimation(PlayerAnimation.Run);
        }
    }

    [Flags]
    public enum PlayerTargetLane
    {
        None = 0,

        HoldAir = 1 << 0,
        HoldGround = 1 << 1,
        HoldBoth = HoldAir | HoldGround,

        AttackAir = 1 << 2,
        AttackGround = 1 << 3,
        AttackBoth = AttackAir | AttackGround,

        GhostAir = HoldGround | AttackAir,
        GhostGround = HoldAir | AttackGround,

        MiniBoss = 1 << 4,
    }

    public enum PlayerAnimation
    {
        Run,
        Jump,
        GroundAttack,
        AirAttack,
        Hold,
        Hurt
    }

    public static class PlayerTargetLaneExtensions
    {
        public static PlayerTargetLane WithHoldLane(this PlayerTargetLane current, LanedHitLane lane, bool held)
        {
            switch (lane)
            {
                case LanedHitLane.Air:
                    return held ? current | PlayerTargetLane.HoldAir : current & ~PlayerTargetLane.HoldAir;

                case LanedHitLane.Ground:
                    return held ? current | PlayerTargetLane.HoldGround : current & ~PlayerTargetLane.HoldGround;
            }

            return current;
        }

        public static PlayerTargetLane WithAttackLane(this PlayerTargetLane current, LanedHitLane lane, bool attack)
        {
            switch (lane)
            {
                case LanedHitLane.Air:
                    return attack ? current | PlayerTargetLane.AttackAir : current & ~PlayerTargetLane.AttackAir;

                case LanedHitLane.Ground:
                    return attack ? current | PlayerTargetLane.AttackGround : current & ~PlayerTargetLane.AttackGround;
            }

            return current;
        }
    }
}

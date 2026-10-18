using System;
using System.Collections.Generic;

namespace FortressUnited.Core.Movement
{
    /// <summary>
    /// Player movement, ported to behave exactly like Team Fortress
    /// Classic's shared player physics (pm_shared): walking, air control and
    /// bunny hopping, friction and edge friction, stairs, crouching, jumping,
    /// water, ladders and getting unstuck.
    ///
    /// Arithmetic mirrors the original: where TFC mixes in double constants
    /// the expression is computed in double and stored as float. The
    /// differential tests compare this against the original game's results.
    ///
    /// One instance can move any number of players; unstick bookkeeping is
    /// kept per player index and per side (server/client), like the original.
    /// </summary>
    public sealed class PlayerMovement
    {
        // Bunny hop cap: TFC allows bunny jumping up to 1.7x maxspeed
        public const float BunnyJumpMaxSpeedFactor = 1.7f;

        const float TimeToDuck = 0.4f;
        const float DuckHullMin = -18, HullMin = -36;
        const float DuckView = 12, StandView = 28, DeadViewHeight = -8;
        const float MaxClimbSpeed = 200;
        const float StopEpsilon = 0.1f;
        const float PlayerFallPunchThreshold = 350;
        const float PlayerMaxSafeFallSpeed = 580;
        const float PlayerMinBounceSpeed = 200;
        const float PlayerLongJumpSpeed = 350;
        const float DuckingMultiplier = 0.333f;
        const int MaxClipPlanes = 5;
        const int MaxClients = 32;
        const double CheckStuckMinTime = 0.05;
        const float WaterJumpHeight = 8;

        static readonly Vec3[] StuckTable = CreateStuckTable();
        static readonly Vec3[] CurrentTable =
        {
            new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(-1, 0, 0),
            new Vec3(0, -1, 0), new Vec3(0, 0, 1), new Vec3(0, 0, -1),
        };

        readonly int[,] stuckLast = new int[MaxClients, 2];
        readonly float[,] stuckCheckTime = new float[MaxClients, 2]; // float, like the original

        MoveState pm;
        IMovementWorld world;
        MoveVars mv;
        bool onLadder;

        /// <summary>Entities touched during the last move (for touch functions).</summary>
        public readonly List<MoveTrace> Touched = new List<MoveTrace>();

        /// <summary>
        /// Moves one player by one command (PM_Move). Origin, velocity, flags
        /// and the rest of the state are updated in place.
        /// </summary>
        public void Move(MoveState state, MoveCommand cmd, IMovementWorld w, MoveVars vars)
        {
            pm = state;
            world = w;
            mv = vars;
            pm.Cmd = cmd;

            PlayerMove(pm.Server);

            if (pm.OnGround != -1)
                pm.Flags |= MoveFlags.OnGround;
            else
                pm.Flags &= ~MoveFlags.OnGround;

            // Single player resets friction after each move so friction triggers work
            if (!pm.Multiplayer && pm.MoveType == MoveType.Walk)
                pm.Friction = 1.0f;
        }

        // ------------------------------------------------------------------
        // Helpers

        MoveTrace Trace(Vec3 start, Vec3 end) => world.PlayerTrace(pm, start, end, -1);

        bool InfoIs(string key, int value)
        {
            int.TryParse(pm.Info(key), out int v);
            return v == value;
        }

        void AddToTouched(MoveTrace tr)
        {
            foreach (var t in Touched)
                if (t.Ent == tr.Ent)
                    return;
            Touched.Add(tr);
        }

        void CheckVelocity()
        {
            for (int i = 0; i < 3; i++)
            {
                if (float.IsNaN(pm.Velocity[i]))
                {
                    var v = pm.Velocity; v[i] = 0; pm.Velocity = v;
                }
                if (float.IsNaN(pm.Origin[i]))
                {
                    var o = pm.Origin; o[i] = 0; pm.Origin = o;
                }

                if (pm.Velocity[i] > mv.MaxVelocity)
                {
                    var v = pm.Velocity; v[i] = mv.MaxVelocity; pm.Velocity = v;
                }
                else if (pm.Velocity[i] < -mv.MaxVelocity)
                {
                    var v = pm.Velocity; v[i] = -mv.MaxVelocity; pm.Velocity = v;
                }
            }
        }

        /// <summary>Slide off a plane; returns blocked flags (1 floor, 2 wall/step).</summary>
        static int ClipVelocity(Vec3 inVel, Vec3 normal, out Vec3 outVel, float overbounce)
        {
            float angle = normal.Z;
            int blocked = 0;
            if (angle > 0)
                blocked |= 0x01;
            if (angle == 0)
                blocked |= 0x02;

            float backoff = Vec3.Dot(inVel, normal) * overbounce;

            outVel = default;
            for (int i = 0; i < 3; i++)
            {
                float change = normal[i] * backoff;
                float o = inVel[i] - change;
                if (o > -StopEpsilon && o < StopEpsilon)
                    o = 0;
                outVel[i] = o;
            }
            return blocked;
        }

        float EntGravity => pm.Gravity != 0 ? pm.Gravity : 1.0f;

        void AddCorrectGravity()
        {
            if (pm.WaterJumpTime != 0)
                return;

            var v = pm.Velocity;
            // yes, this 0.5 looks wrong, but it's not
            v.Z = (float)(v.Z - (EntGravity * mv.Gravity * 0.5 * pm.FrameTime));
            v.Z += pm.BaseVelocity.Z * pm.FrameTime;
            pm.Velocity = v;
            var b = pm.BaseVelocity; b.Z = 0; pm.BaseVelocity = b;

            CheckVelocity();
        }

        void FixupGravityVelocity()
        {
            if (pm.WaterJumpTime != 0)
                return;

            var v = pm.Velocity;
            v.Z = (float)(v.Z - (EntGravity * mv.Gravity * pm.FrameTime * 0.5));
            pm.Velocity = v;

            CheckVelocity();
        }

        void AddGravity()
        {
            var v = pm.Velocity;
            v.Z -= EntGravity * mv.Gravity * pm.FrameTime;
            v.Z += pm.BaseVelocity.Z * pm.FrameTime;
            pm.Velocity = v;
            var b = pm.BaseVelocity; b.Z = 0; pm.BaseVelocity = b;
            CheckVelocity();
        }

        // ------------------------------------------------------------------
        // Sliding movement

        /// <summary>Basic solid body move that slides along up to four planes (PM_FlyMove).</summary>
        int FlyMove()
        {
            const int numBumps = 4;
            int blocked = 0;
            int numPlanes = 0;
            var planes = new Vec3[MaxClipPlanes];
            Vec3 originalVelocity = pm.Velocity;
            Vec3 primalVelocity = pm.Velocity;
            Vec3 newVelocity = default;
            float allFraction = 0;
            float timeLeft = pm.FrameTime;

            for (int bumpCount = 0; bumpCount < numBumps; bumpCount++)
            {
                if (pm.Velocity.IsZero)
                    break;

                Vec3 end = default;
                for (int i = 0; i < 3; i++)
                    end[i] = pm.Origin[i] + timeLeft * pm.Velocity[i];

                MoveTrace trace = Trace(pm.Origin, end);

                allFraction += trace.Fraction;

                // Trapped in solid
                if (trace.AllSolid)
                {
                    pm.Velocity = Vec3.Zero;
                    return 4;
                }

                if (trace.Fraction > 0)
                {
                    pm.Origin = trace.EndPos;
                    originalVelocity = pm.Velocity;
                    numPlanes = 0;
                }

                if (trace.Fraction == 1)
                    break;

                AddToTouched(trace);

                if (trace.PlaneNormal.Z > 0.7)
                    blocked |= 1; // floor
                if (trace.PlaneNormal.Z == 0)
                    blocked |= 2; // step / wall

                timeLeft -= timeLeft * trace.Fraction;

                if (numPlanes >= MaxClipPlanes)
                {
                    pm.Velocity = Vec3.Zero;
                    break;
                }

                planes[numPlanes] = trace.PlaneNormal;
                numPlanes++;

                // Reflect only off the first plane, when airborne or on low friction
                if (numPlanes == 1 && pm.MoveType == MoveType.Walk && (pm.OnGround == -1 || pm.Friction != 1))
                {
                    for (int i = 0; i < numPlanes; i++)
                    {
                        if (planes[i].Z > 0.7)
                        {
                            // floor or slope
                            ClipVelocity(originalVelocity, planes[i], out newVelocity, 1);
                            originalVelocity = newVelocity;
                        }
                        else
                        {
                            ClipVelocity(originalVelocity, planes[i], out newVelocity,
                                (float)(1.0 + mv.Bounce * (1 - pm.Friction)));
                        }
                    }

                    pm.Velocity = newVelocity;
                    originalVelocity = newVelocity;
                }
                else
                {
                    int i;
                    for (i = 0; i < numPlanes; i++)
                    {
                        ClipVelocity(originalVelocity, planes[i], out Vec3 clipped, 1);
                        pm.Velocity = clipped;
                        int j;
                        for (j = 0; j < numPlanes; j++)
                        {
                            if (j != i && Vec3.Dot(pm.Velocity, planes[j]) < 0)
                                break; // moving against this plane
                        }
                        if (j == numPlanes)
                            break; // didn't have to clip
                    }

                    if (i == numPlanes)
                    {
                        // go along the crease
                        if (numPlanes != 2)
                        {
                            pm.Velocity = Vec3.Zero;
                            break;
                        }
                        Vec3 dir = Vec3.Cross(planes[0], planes[1]);
                        float d = Vec3.Dot(dir, pm.Velocity);
                        pm.Velocity = dir * d;
                    }

                    // Stop dead if now moving against the original direction
                    if (Vec3.Dot(pm.Velocity, primalVelocity) <= 0)
                    {
                        pm.Velocity = Vec3.Zero;
                        break;
                    }
                }
            }

            if (allFraction == 0)
                pm.Velocity = Vec3.Zero;

            return blocked;
        }

        void Accelerate(Vec3 wishDir, float wishSpeed, float accel)
        {
            if (pm.Dead || pm.WaterJumpTime != 0)
                return;

            float currentSpeed = Vec3.Dot(pm.Velocity, wishDir);
            float addSpeed = wishSpeed - currentSpeed;
            if (addSpeed <= 0)
                return;

            float accelSpeed = accel * pm.FrameTime * wishSpeed * pm.Friction;
            if (accelSpeed > addSpeed)
                accelSpeed = addSpeed;

            pm.Velocity = Vec3.MA(pm.Velocity, accelSpeed, wishDir);
        }

        void AirAccelerate(Vec3 wishDir, float wishSpeed, float accel)
        {
            if (pm.Dead || pm.WaterJumpTime != 0)
                return;

            // Air control is capped at 30 units/s along the wish direction,
            // but the acceleration uses the full wish speed: this is what
            // makes strafe jumping and bunny hopping work.
            float wishSpd = wishSpeed;
            if (wishSpd > 30)
                wishSpd = 30;

            float currentSpeed = Vec3.Dot(pm.Velocity, wishDir);
            float addSpeed = wishSpd - currentSpeed;
            if (addSpeed <= 0)
                return;

            float accelSpeed = accel * wishSpeed * pm.FrameTime * pm.Friction;
            if (accelSpeed > addSpeed)
                accelSpeed = addSpeed;

            pm.Velocity = Vec3.MA(pm.Velocity, accelSpeed, wishDir);
        }

        // ------------------------------------------------------------------
        // Ground, air and water movement

        void WalkMove()
        {
            float fmove = pm.Cmd.ForwardMove;
            float smove = pm.Cmd.SideMove;

            var f = pm.Forward; f.Z = 0; f.Normalize(); pm.Forward = f;
            var r = pm.Right; r.Z = 0; r.Normalize(); pm.Right = r;

            Vec3 wishVel = new Vec3(
                pm.Forward.X * fmove + pm.Right.X * smove,
                pm.Forward.Y * fmove + pm.Right.Y * smove,
                0);

            Vec3 wishDir = wishVel;
            float wishSpeed = wishDir.Normalize();

            if (wishSpeed > pm.MaxSpeed)
            {
                wishVel = wishVel * (pm.MaxSpeed / wishSpeed);
                wishSpeed = pm.MaxSpeed;
            }

            var v = pm.Velocity; v.Z = 0; pm.Velocity = v;
            Accelerate(wishDir, wishSpeed, mv.Accelerate);
            v = pm.Velocity; v.Z = 0; pm.Velocity = v;

            pm.Velocity = pm.Velocity + pm.BaseVelocity;

            float spd = pm.Velocity.Length();
            if (spd < 1.0f)
            {
                pm.Velocity = Vec3.Zero;
                return;
            }

            int oldOnGround = pm.OnGround;

            // First try moving directly to the destination
            Vec3 dest = new Vec3(
                pm.Origin.X + pm.Velocity.X * pm.FrameTime,
                pm.Origin.Y + pm.Velocity.Y * pm.FrameTime,
                pm.Origin.Z);

            MoveTrace trace = Trace(pm.Origin, dest);
            if (trace.Fraction == 1)
            {
                pm.Origin = trace.EndPos;
                return;
            }

            if (oldOnGround == -1 && pm.WaterLevel == 0)
                return; // don't walk up stairs if not on ground

            if (pm.WaterJumpTime != 0)
                return;

            // Try sliding forward both on ground and up a step; take the move that goes farthest
            Vec3 original = pm.Origin;
            Vec3 originalVel = pm.Velocity;

            FlyMove();

            Vec3 down = pm.Origin;
            Vec3 downVel = pm.Velocity;

            pm.Origin = original;
            pm.Velocity = originalVel;

            // Start out up one stair height
            dest = pm.Origin;
            dest.Z += mv.StepSize;

            trace = Trace(pm.Origin, dest);
            if (!trace.StartSolid && !trace.AllSolid)
                pm.Origin = trace.EndPos;

            FlyMove();

            // Press down the step height
            dest = pm.Origin;
            dest.Z -= mv.StepSize;

            trace = Trace(pm.Origin, dest);

            if (trace.PlaneNormal.Z < 0.7)
            {
                // Not on the ground any more: use the original attempt
                pm.Origin = down;
                pm.Velocity = downVel;
                return;
            }

            if (!trace.StartSolid && !trace.AllSolid)
                pm.Origin = trace.EndPos;

            pm.Up = pm.Origin; // the original stores the up position here

            float downDist = (down.X - original.X) * (down.X - original.X)
                           + (down.Y - original.Y) * (down.Y - original.Y);
            float upDist = (pm.Up.X - original.X) * (pm.Up.X - original.X)
                         + (pm.Up.Y - original.Y) * (pm.Up.Y - original.Y);

            if (downDist > upDist)
            {
                pm.Origin = down;
                pm.Velocity = downVel;
            }
            else
            {
                v = pm.Velocity; v.Z = downVel.Z; pm.Velocity = v;
            }
        }

        void Friction()
        {
            if (pm.WaterJumpTime != 0)
                return;

            Vec3 vel = pm.Velocity;
            float speed = (float)Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z);

            if (speed < 0.1f)
                return;

            float drop = 0;

            if (pm.OnGround != -1)
            {
                // Edge friction: more friction if there's a drop just ahead
                Vec3 start = new Vec3(
                    pm.Origin.X + vel.X / speed * 16,
                    pm.Origin.Y + vel.Y / speed * 16,
                    pm.Origin.Z + pm.PlayerMins[pm.UseHull].Z);
                Vec3 stop = start;
                stop.Z = start.Z - 34;

                MoveTrace trace = Trace(start, stop);

                float friction;
                if (trace.Fraction == 1.0)
                    friction = mv.Friction * mv.EdgeFriction;
                else
                    friction = mv.Friction;

                friction *= pm.Friction;

                float control = speed < mv.StopSpeed ? mv.StopSpeed : speed;
                drop += control * friction * pm.FrameTime;
            }

            float newSpeed = speed - drop;
            if (newSpeed < 0)
                newSpeed = 0;

            newSpeed /= speed;

            pm.Velocity = new Vec3(vel.X * newSpeed, vel.Y * newSpeed, vel.Z * newSpeed);
        }

        void WaterMove()
        {
            Vec3 wishVel = default;
            for (int i = 0; i < 3; i++)
                wishVel[i] = pm.Forward[i] * pm.Cmd.ForwardMove + pm.Right[i] * pm.Cmd.SideMove;

            // Sink when not moving, otherwise go up by upmove
            if (pm.Cmd.ForwardMove == 0 && pm.Cmd.SideMove == 0 && pm.Cmd.UpMove == 0)
                wishVel.Z -= 60;
            else
                wishVel.Z += pm.Cmd.UpMove;

            Vec3 wishDir = wishVel;
            float wishSpeed = wishDir.Normalize();

            if (wishSpeed > pm.MaxSpeed)
            {
                wishVel = wishVel * (pm.MaxSpeed / wishSpeed);
                wishSpeed = pm.MaxSpeed;
            }
            wishSpeed = (float)(wishSpeed * 0.8);

            pm.Velocity = pm.Velocity + pm.BaseVelocity;

            // Water friction
            Vec3 temp = pm.Velocity;
            float speed = temp.Normalize();
            float newSpeed;
            if (speed != 0)
            {
                newSpeed = speed - pm.FrameTime * speed * mv.Friction * pm.Friction;
                if (newSpeed < 0)
                    newSpeed = 0;
                pm.Velocity = pm.Velocity * (newSpeed / speed);
            }
            else
            {
                newSpeed = 0;
            }

            if (wishSpeed < 0.1f)
                return;

            float addSpeed = wishSpeed - newSpeed;
            if (addSpeed > 0)
            {
                wishVel.Normalize();
                float accelSpeed = mv.Accelerate * wishSpeed * pm.FrameTime * pm.Friction;
                if (accelSpeed > addSpeed)
                    accelSpeed = addSpeed;

                pm.Velocity = Vec3.MA(pm.Velocity, accelSpeed, wishVel);
            }

            // Assume it's a stair or slope: press down from step height above
            Vec3 dest = Vec3.MA(pm.Origin, pm.FrameTime, pm.Velocity);
            Vec3 start = dest;
            start.Z += mv.StepSize + 1;
            MoveTrace trace = Trace(start, dest);
            if (!trace.StartSolid && !trace.AllSolid)
            {
                pm.Origin = trace.EndPos;
                return;
            }

            FlyMove();
        }

        void AirMove()
        {
            float fmove = pm.Cmd.ForwardMove;
            float smove = pm.Cmd.SideMove;

            var f = pm.Forward; f.Z = 0; f.Normalize(); pm.Forward = f;
            var r = pm.Right; r.Z = 0; r.Normalize(); pm.Right = r;

            Vec3 wishVel = new Vec3(
                pm.Forward.X * fmove + pm.Right.X * smove,
                pm.Forward.Y * fmove + pm.Right.Y * smove,
                0);

            Vec3 wishDir = wishVel;
            float wishSpeed = wishDir.Normalize();

            if (wishSpeed > pm.MaxSpeed)
            {
                wishVel = wishVel * (pm.MaxSpeed / wishSpeed);
                wishSpeed = pm.MaxSpeed;
            }

            AirAccelerate(wishDir, wishSpeed, mv.AirAccelerate);

            pm.Velocity = pm.Velocity + pm.BaseVelocity;

            FlyMove();
        }

        bool InWater() => pm.WaterLevel > 1;

        static bool IsLiquid(int contents) => contents <= Contents.Water && contents > Contents.Translucent;

        /// <summary>Sets water level (0-3) and type; returns true if at least waist deep.</summary>
        bool CheckWater()
        {
            Vec3 mins = pm.PlayerMins[pm.UseHull], maxs = pm.PlayerMaxs[pm.UseHull];
            Vec3 point = new Vec3(
                (float)(pm.Origin.X + (mins.X + maxs.X) * 0.5),
                (float)(pm.Origin.Y + (mins.Y + maxs.Y) * 0.5),
                pm.Origin.Z + mins.Z + 1);

            pm.WaterLevel = 0;
            pm.WaterType = Contents.Empty;

            int cont = world.PointContents(point, out int trueCont);
            if (IsLiquid(cont))
            {
                pm.WaterType = cont;
                pm.WaterLevel = 1;

                float height = mins.Z + maxs.Z;
                float heightOver2 = (float)(height * 0.5);

                point.Z = pm.Origin.Z + heightOver2;
                cont = world.PointContents(point, out _);
                if (IsLiquid(cont))
                {
                    pm.WaterLevel = 2;

                    point.Z = pm.Origin.Z + pm.ViewOfs.Z;
                    cont = world.PointContents(point, out _);
                    if (IsLiquid(cont))
                        pm.WaterLevel = 3;
                }

                // Water currents push harder the deeper you are
                if (trueCont <= Contents.Current0 && trueCont >= Contents.CurrentDown)
                {
                    pm.BaseVelocity = Vec3.MA(pm.BaseVelocity, (float)(50.0 * pm.WaterLevel),
                        CurrentTable[Contents.Current0 - trueCont]);
                }
            }

            return pm.WaterLevel > 1;
        }

        void CategorizePosition()
        {
            CheckWater();

            Vec3 point = new Vec3(pm.Origin.X, pm.Origin.Y, pm.Origin.Z - 2);

            if (pm.Velocity.Z > 180)
            {
                pm.OnGround = -1; // shooting up fast: not on ground
            }
            else
            {
                MoveTrace tr = Trace(pm.Origin, point);
                if (tr.PlaneNormal.Z < 0.7)
                    pm.OnGround = -1; // too steep
                else
                    pm.OnGround = tr.Ent;

                if (pm.OnGround != -1)
                {
                    pm.WaterJumpTime = 0;
                    // Snap down to the ground
                    if (pm.WaterLevel < 2 && !tr.StartSolid && !tr.AllSolid)
                        pm.Origin = tr.EndPos;
                }

                if (tr.Ent > 0)
                    AddToTouched(tr);
            }
        }

        // ------------------------------------------------------------------
        // Getting unstuck

        static Vec3[] CreateStuckTable()
        {
            var table = new Vec3[54];
            int idx = 0;
            float x, y, z;

            // Little moves
            x = y = 0;
            for (z = -0.125f; z <= 0.125f; z += 0.125f)
                table[idx++] = new Vec3(x, y, z);
            x = z = 0;
            for (y = -0.125f; y <= 0.125f; y += 0.125f)
                table[idx++] = new Vec3(x, y, z);
            y = z = 0;
            for (x = -0.125f; x <= 0.125f; x += 0.125f)
                table[idx++] = new Vec3(x, y, z);

            for (x = -0.125f; x <= 0.125f; x += 0.250f)
                for (y = -0.125f; y <= 0.125f; y += 0.250f)
                    for (z = -0.125f; z <= 0.125f; z += 0.250f)
                        table[idx++] = new Vec3(x, y, z);

            // Big moves
            x = y = 0;
            float[] zi = { 0.0f, 1.0f, 6.0f };
            for (int i = 0; i < 3; i++)
                table[idx++] = new Vec3(x, y, zi[i]);

            x = z = 0;
            for (y = -2.0f; y <= 2.0f; y += 2.0f)
                table[idx++] = new Vec3(x, y, z);
            y = z = 0;
            for (x = -2.0f; x <= 2.0f; x += 2.0f)
                table[idx++] = new Vec3(x, y, z);

            for (int i = 0; i < 3; i++)
            {
                z = zi[i];
                for (x = -2.0f; x <= 2.0f; x += 2.0f)
                    for (y = -2.0f; y <= 2.0f; y += 2.0f)
                        table[idx++] = new Vec3(x, y, z);
            }

            return table;
        }

        int Side => pm.Server ? 1 : 0;

        int GetRandomStuckOffsets(int index, int side, out Vec3 offset)
        {
            int idx = stuckLast[index, side]++;
            offset = StuckTable[idx % 54];
            return idx % 54;
        }

        void ResetStuckOffsets(int index, int side) => stuckLast[index, side] = 0;

        /// <summary>Returns true if still stuck (PM_CheckStuck).</summary>
        bool CheckStuck()
        {
            int hitEnt = world.TestPlayerPosition(pm, pm.Origin);
            if (hitEnt == -1)
            {
                ResetStuckOffsets(pm.PlayerIndex, Side);
                return false;
            }

            Vec3 baseOrigin = pm.Origin;
            Vec3 offset, test;
            int i;

            // Precision errors: nudge out of world/brush models. The legacy
            // TFC server only does this on the client side.
            if (!pm.Server)
            {
                if (hitEnt == 0 || world.IsBrushModel(hitEnt))
                {
                    int reps = 0;
                    ResetStuckOffsets(pm.PlayerIndex, Side);
                    do
                    {
                        GetRandomStuckOffsets(pm.PlayerIndex, Side, out offset);
                        test = baseOrigin + offset;
                        if (world.TestPlayerPosition(pm, test) == -1)
                        {
                            ResetStuckOffsets(pm.PlayerIndex, Side);
                            pm.Origin = test;
                            return false;
                        }
                        reps++;
                    } while (reps < 54);
                }
            }

            // The original indexes the throttle by 0 = server, 1 = client
            int idx = pm.Server ? 0 : 1;

            float time = (float)world.Time;
            if (stuckCheckTime[pm.PlayerIndex, idx] >= time - CheckStuckMinTime)
                return true; // too soon
            stuckCheckTime[pm.PlayerIndex, idx] = time;

            world.StuckTouch(pm, hitEnt);

            i = GetRandomStuckOffsets(pm.PlayerIndex, Side, out offset);
            test = baseOrigin + offset;
            if ((hitEnt = world.TestPlayerPosition(pm, test)) == -1)
            {
                ResetStuckOffsets(pm.PlayerIndex, Side);
                if (i >= 27)
                    pm.Origin = test;
                return false;
            }

            // Stuck in another player and pressing jump/duck/attack: force out
            if ((pm.Cmd.Buttons & (Buttons.Jump | Buttons.Duck | Buttons.Attack)) != 0 && world.IsPlayer(hitEnt))
            {
                const float xyStep = 8.0f, zStep = 18.0f;
                const float xyMinMax = xyStep, zMinMax = 4 * zStep;

                for (float z = 0; z <= zMinMax; z += zStep)
                    for (float x = -xyMinMax; x <= xyMinMax; x += xyStep)
                        for (float y = -xyMinMax; y <= xyMinMax; y += xyStep)
                        {
                            test = baseOrigin + new Vec3(x, y, z);
                            if (world.TestPlayerPosition(pm, test) == -1)
                            {
                                pm.Origin = test;
                                return false;
                            }
                        }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Crouching

        static float SplineFraction(float value, float scale)
        {
            value = scale * value;
            float valueSquared = value * value;
            return 3 * valueSquared - 2 * valueSquared * value;
        }

        void FixPlayerCrouchStuck(int direction)
        {
            if (world.TestPlayerPosition(pm, pm.Origin) == -1)
                return;

            Vec3 test = pm.Origin;
            for (int i = 0; i < 36; i++)
            {
                var o = pm.Origin; o.Z += direction; pm.Origin = o;
                if (world.TestPlayerPosition(pm, pm.Origin) == -1)
                    return;
            }

            pm.Origin = test; // failed
        }

        void UnDuck()
        {
            Vec3 newOrigin = pm.Origin;

            if (pm.OnGround != -1)
                newOrigin = newOrigin + (pm.PlayerMins[Hulls.Ducked] - pm.PlayerMins[Hulls.Standing]);

            MoveTrace trace = Trace(newOrigin, newOrigin);

            if (!trace.StartSolid)
            {
                pm.UseHull = Hulls.Standing;

                // Changing hulls may have stuck us; stay ducked if so
                trace = Trace(newOrigin, newOrigin);
                if (trace.StartSolid)
                {
                    pm.UseHull = Hulls.Ducked;
                    return;
                }

                pm.Flags &= ~MoveFlags.Ducking;
                pm.InDuck = false;
                var vo = pm.ViewOfs; vo.Z = StandView; pm.ViewOfs = vo;
                pm.DuckTime = 0;

                pm.Origin = newOrigin;

                CategorizePosition();
            }
        }

        void Duck()
        {
            Buttons buttonsChanged = pm.OldButtons ^ pm.Cmd.Buttons;
            Buttons buttonsPressed = buttonsChanged & pm.Cmd.Buttons;

            if ((pm.Cmd.Buttons & Buttons.Duck) != 0)
                pm.OldButtons |= Buttons.Duck;
            else
                pm.OldButtons &= ~Buttons.Duck;

            if (pm.IUser3 != 0 || pm.Dead)
            {
                if ((pm.Flags & MoveFlags.Ducking) != 0)
                    UnDuck();
                return;
            }

            if ((pm.Flags & MoveFlags.Ducking) != 0)
            {
                pm.Cmd.ForwardMove = (float)(pm.Cmd.ForwardMove * DuckingMultiplierD);
                pm.Cmd.SideMove = (float)(pm.Cmd.SideMove * DuckingMultiplierD);
                pm.Cmd.UpMove = (float)(pm.Cmd.UpMove * DuckingMultiplierD);
            }

            if ((pm.Cmd.Buttons & Buttons.Duck) != 0 || pm.InDuck || (pm.Flags & MoveFlags.Ducking) != 0)
            {
                if ((pm.Cmd.Buttons & Buttons.Duck) != 0)
                {
                    if ((buttonsPressed & Buttons.Duck) != 0 && (pm.Flags & MoveFlags.Ducking) == 0)
                    {
                        // 1 second so the super long jump works
                        pm.DuckTime = 1000;
                        pm.InDuck = true;
                    }

                    double time = Math.Max(0.0, 1.0 - pm.DuckTime / 1000.0);

                    if (pm.InDuck)
                    {
                        // Finish ducking immediately if duck time is over or not on ground.
                        // The original tests DuckTime / 1000.0 <= 1.0 - 0.4 on the x87 FPU,
                        // where 600 / 1000 comes out just above the rounded constant; for
                        // float duck times that is exactly DuckTime < 600 (plain double
                        // maths would wrongly accept 600).
                        if (pm.DuckTime < 600 || pm.OnGround == -1)
                        {
                            pm.UseHull = Hulls.Ducked;
                            var vo = pm.ViewOfs; vo.Z = DuckView; pm.ViewOfs = vo;
                            pm.Flags |= MoveFlags.Ducking;
                            pm.InDuck = false;

                            // The original's collision fudge: drop the origin to keep feet in place
                            if (pm.OnGround != -1)
                            {
                                pm.Origin = pm.Origin - (pm.PlayerMins[Hulls.Ducked] - pm.PlayerMins[Hulls.Standing]);
                                FixPlayerCrouchStuck(1);
                                CategorizePosition();
                            }
                        }
                        else
                        {
                            float more = DuckHullMin - HullMin;
                            float duckFraction = SplineFraction((float)time, (float)(1.0 / TimeToDuckD));
                            var vo = pm.ViewOfs;
                            vo.Z = ((DuckView - more) * duckFraction) + (StandView * (1 - duckFraction));
                            pm.ViewOfs = vo;
                        }
                    }
                }
                else
                {
                    UnDuck();
                }
            }
        }

        // The original's #defines are doubles in arithmetic
        const double DuckingMultiplierD = 0.333;
        const double TimeToDuckD = 0.4;

        // ------------------------------------------------------------------
        // Ladders

        void LadderMove(ILadder ladder)
        {
            if (pm.MoveType == MoveType.Noclip)
                return;

            // TFC freezes players with maxspeed 1; frozen players can't climb
            if (pm.MaxSpeed <= 1.0)
                return;

            Vec3 ladderCenter = (ladder.Mins + ladder.Maxs) * 0.5f;

            pm.MoveType = MoveType.Fly;

            Vec3 floor = pm.Origin;
            floor.Z += pm.PlayerMins[pm.UseHull].Z - 1;
            bool onFloor = world.PointContents(floor, out _) == Contents.Solid;

            pm.Gravity = 0;
            MoveTrace trace = ladder.Trace(pm.Origin, ladderCenter);
            if (trace.Fraction != 1.0)
            {
                float forward = 0, right = 0;
                float speed = MaxClimbSpeed;

                if (speed > pm.MaxSpeed)
                    speed = pm.MaxSpeed;

                Vec3.AngleVectors(pm.Angles, out Vec3 vpn, out Vec3 vRight, out _);

                if ((pm.Flags & MoveFlags.Ducking) != 0)
                    speed = (float)(speed * DuckingMultiplierD);

                if ((pm.Cmd.Buttons & Buttons.Back) != 0) forward -= speed;
                if ((pm.Cmd.Buttons & Buttons.Forward) != 0) forward += speed;
                if ((pm.Cmd.Buttons & Buttons.MoveLeft) != 0) right -= speed;
                if ((pm.Cmd.Buttons & Buttons.MoveRight) != 0) right += speed;

                if ((pm.Cmd.Buttons & Buttons.Jump) != 0)
                {
                    pm.MoveType = MoveType.Walk;
                    pm.Velocity = trace.PlaneNormal * 270;
                }
                else if (forward != 0 || right != 0)
                {
                    Vec3 velocity = Vec3.MA(vpn * forward, right, vRight);

                    // Perpendicular in the ladder plane
                    Vec3 perp = Vec3.Cross(new Vec3(0, 0, 1), trace.PlaneNormal);
                    perp.Normalize();

                    // Decompose velocity into the ladder plane
                    float normal = Vec3.Dot(velocity, trace.PlaneNormal);
                    Vec3 cross = trace.PlaneNormal * normal;
                    Vec3 lateral = velocity - cross;

                    // Velocity into the ladder face becomes climbing
                    Vec3 tmp = Vec3.Cross(trace.PlaneNormal, perp);
                    pm.Velocity = Vec3.MA(lateral, -normal, tmp);
                    if (onFloor && normal > 0)
                        pm.Velocity = Vec3.MA(pm.Velocity, MaxClimbSpeed, trace.PlaneNormal);
                }
                else
                {
                    pm.Velocity = Vec3.Zero;
                }
            }
        }

        // ------------------------------------------------------------------
        // Jumping

        void WaterJump()
        {
            if (pm.WaterJumpTime > 10000)
                pm.WaterJumpTime = 10000;

            if (pm.WaterJumpTime == 0)
                return;

            pm.WaterJumpTime -= pm.Cmd.Msec;
            if (pm.WaterJumpTime < 0 || pm.WaterLevel == 0)
            {
                pm.WaterJumpTime = 0;
                pm.Flags &= ~MoveFlags.WaterJump;
            }

            var v = pm.Velocity;
            v.X = pm.MoveDir.X;
            v.Y = pm.MoveDir.Y;
            pm.Velocity = v;
        }

        /// <summary>
        /// Caps bunny hop speed when jumping (PM_PreventMegaBunnyJumping):
        /// above 1.7x maxspeed, velocity drops to 1.7x * 0.65.
        /// </summary>
        void PreventMegaBunnyJumping()
        {
            float maxScaledSpeed = BunnyJumpMaxSpeedFactor * pm.MaxSpeed;
            if (maxScaledSpeed <= 0.0f)
                return;

            float spd = pm.Velocity.Length();
            if (spd <= maxScaledSpeed)
                return;

            float fraction = (float)((maxScaledSpeed / spd) * 0.65);
            pm.Velocity = pm.Velocity * fraction;
        }

        void Jump()
        {
            if (pm.Dead)
            {
                pm.OldButtons |= Buttons.Jump; // don't jump again until released
                return;
            }

            bool tfc = InfoIs("tfc", 1);

            // A spy feigning death can't jump
            if (tfc && pm.DeadFlag == DeadFlagFeigning)
                return;

            if (pm.WaterJumpTime != 0)
            {
                pm.WaterJumpTime -= pm.Cmd.Msec;
                if (pm.WaterJumpTime < 0)
                    pm.WaterJumpTime = 0;
                return;
            }

            // Swimming, not jumping
            if (pm.WaterLevel >= 2)
            {
                pm.OnGround = -1;

                var v = pm.Velocity;
                if (pm.WaterType == Contents.Water)
                    v.Z = 100;
                else if (pm.WaterType == Contents.Slime)
                    v.Z = 80;
                else
                    v.Z = 50; // lava
                pm.Velocity = v;

                if (pm.SwimTime <= 0)
                {
                    pm.SwimTime = 1000;
                    world.PlaySound(pm, $"player/pl_wade{world.RandomLong(0, 3) + 1}.wav", 1);
                }
                return;
            }

            if (pm.OnGround == -1)
            {
                pm.OldButtons |= Buttons.Jump;
                return; // in the air
            }

            if ((pm.OldButtons & Buttons.Jump) != 0)
                return; // don't pogo stick

            pm.OnGround = -1;

            PreventMegaBunnyJumping();

            if (tfc)
                world.PlaySound(pm, "player/plyrjmp8.wav", 0.5f);

            bool canSuperJump = InfoIs("slj", 1);

            var vel = pm.Velocity;
            if (pm.InDuck || (pm.Flags & MoveFlags.Ducking) != 0)
            {
                if (canSuperJump && (pm.Cmd.Buttons & Buttons.Duck) != 0 && pm.DuckTime > 0 && pm.Velocity.Length() > 50)
                {
                    var p = pm.PunchAngle; p.X = -5; pm.PunchAngle = p;
                    for (int i = 0; i < 2; i++)
                        vel[i] = (float)(pm.Forward[i] * PlayerLongJumpSpeed * 1.6);
                    vel.Z = (float)Math.Sqrt(2 * 800 * 56.0);
                }
                else
                {
                    vel.Z = (float)Math.Sqrt(2 * 800 * 45.0);
                }
            }
            else
            {
                vel.Z = (float)Math.Sqrt(2 * 800 * 45.0);
            }
            pm.Velocity = vel;

            FixupGravityVelocity();

            pm.OldButtons |= Buttons.Jump;
        }

        // DEAD_DISCARDBODY + 1: TFC's feign death state
        const int DeadFlagFeigning = 5;

        void CheckWaterJump()
        {
            if (pm.WaterJumpTime != 0)
                return;

            // Don't hop out if we just jumped in
            if (pm.Velocity.Z < -180)
                return;

            Vec3 flatVelocity = new Vec3(pm.Velocity.X, pm.Velocity.Y, 0);
            float curSpeed = flatVelocity.Normalize();

            Vec3 flatForward = new Vec3(pm.Forward.X, pm.Forward.Y, 0);
            flatForward.Normalize();

            // Backing into water from steps: don't pop forward
            if (curSpeed != 0.0 && Vec3.Dot(flatVelocity, flatForward) < 0.0)
                return;

            Vec3 vecStart = pm.Origin;
            vecStart.Z += WaterJumpHeight;
            Vec3 vecEnd = Vec3.MA(vecStart, 24, flatForward);

            // Point-sized trace
            int saveHull = pm.UseHull;
            pm.UseHull = Hulls.Point;
            MoveTrace tr = Trace(vecStart, vecEnd);
            if (tr.Fraction < 1.0 && Math.Abs(tr.PlaneNormal.Z) < 0.1f)
            {
                vecStart.Z += pm.PlayerMaxs[saveHull].Z - WaterJumpHeight;
                vecEnd = Vec3.MA(vecStart, 24, flatForward);
                pm.MoveDir = tr.PlaneNormal * -50;

                tr = Trace(vecStart, vecEnd);
                if (tr.Fraction == 1.0)
                {
                    pm.WaterJumpTime = 2000;
                    var v = pm.Velocity; v.Z = 225; pm.Velocity = v;
                    pm.OldButtons |= Buttons.Jump;
                    pm.Flags |= MoveFlags.WaterJump;
                }
            }

            pm.UseHull = saveHull;
        }

        void CheckFalling()
        {
            if (pm.OnGround != -1 && !pm.Dead && pm.FallVelocity >= PlayerFallPunchThreshold)
            {
                float fvol = 0.5f;

                if (pm.WaterLevel > 0)
                {
                }
                else if (pm.FallVelocity > PlayerMaxSafeFallSpeed)
                {
                    world.PlaySound(pm, "player/pl_fallpain3.wav", 1);
                    fvol = 1.0f;
                }
                else if (pm.FallVelocity > PlayerMaxSafeFallSpeed / 2)
                {
                    if (InfoIs("tfc", 1))
                        world.PlaySound(pm, "player/pl_fallpain3.wav", 1);
                    fvol = 0.85f;
                }
                else if (pm.FallVelocity < PlayerMinBounceSpeed)
                {
                    fvol = 0;
                }

                if (fvol > 0.0)
                {
                    pm.TimeStepSound = 0;
                    // (landing step sound: see UpdateStepSound)

                    // Knock the screen around a little bit
                    var p = pm.PunchAngle;
                    p.Z = (float)(pm.FallVelocity * 0.013);
                    if (p.X > 8)
                        p.X = 8;
                    pm.PunchAngle = p;
                }
            }

            if (pm.OnGround != -1)
                pm.FallVelocity = 0;
        }

        void PlayWaterSounds()
        {
            if ((pm.OldWaterLevel == 0 && pm.WaterLevel != 0) || (pm.OldWaterLevel != 0 && pm.WaterLevel == 0))
                world.PlaySound(pm, $"player/pl_wade{world.RandomLong(0, 3) + 1}.wav", 1);
        }

        // ------------------------------------------------------------------
        // Per-command setup

        static float CalcRoll(Vec3 angles, Vec3 velocity, float rollAngle, float rollSpeed)
        {
            Vec3.AngleVectors(angles, out _, out Vec3 right, out _);
            float side = Vec3.Dot(velocity, right);
            float sign = side < 0 ? -1 : 1;
            side = Math.Abs(side);

            if (side < rollSpeed)
                side = side * rollAngle / rollSpeed;
            else
                side = rollAngle;

            return side * sign;
        }

        void DropPunchAngle()
        {
            Vec3 punch = pm.PunchAngle;
            float len = punch.Normalize();
            len = (float)(len - (10.0 + len * 0.5) * pm.FrameTime);
            len = Math.Max(len, 0.0f);
            pm.PunchAngle = punch * len;
        }

        void CheckParameters()
        {
            float spd = (float)Math.Sqrt(
                pm.Cmd.ForwardMove * pm.Cmd.ForwardMove +
                pm.Cmd.SideMove * pm.Cmd.SideMove +
                pm.Cmd.UpMove * pm.Cmd.UpMove);

            float maxSpeed = pm.ClientMaxSpeed;
            if (maxSpeed != 0.0)
                pm.MaxSpeed = Math.Min(maxSpeed, pm.MaxSpeed);

            if (spd != 0.0 && spd > pm.MaxSpeed)
            {
                float ratio = pm.MaxSpeed / spd;
                pm.Cmd.ForwardMove *= ratio;
                pm.Cmd.SideMove *= ratio;
                pm.Cmd.UpMove *= ratio;
            }

            if ((pm.Flags & MoveFlags.Frozen) != 0 || (pm.Flags & MoveFlags.OnTrain) != 0 || pm.Dead)
            {
                pm.Cmd.ForwardMove = 0;
                pm.Cmd.SideMove = 0;
                pm.Cmd.UpMove = 0;
            }

            DropPunchAngle();

            if (!pm.Dead)
            {
                Vec3 vAngle = pm.Cmd.ViewAngles + pm.PunchAngle;
                var a = pm.Angles;
                a.Z = CalcRoll(vAngle, pm.Velocity, mv.RollAngle, mv.RollSpeed) * 4;
                a.X = vAngle.X;
                a.Y = vAngle.Y;
                pm.Angles = a;
            }
            else
            {
                pm.Angles = pm.OldAngles;
            }

            if (pm.Dead)
            {
                var vo = pm.ViewOfs; vo.Z = DeadViewHeight; pm.ViewOfs = vo;
            }

            if (pm.Angles.Y > 180.0f)
            {
                var a = pm.Angles; a.Y -= 360.0f; pm.Angles = a;
            }
        }

        void ReduceTimers()
        {
            if (pm.TimeStepSound > 0)
            {
                pm.TimeStepSound -= pm.Cmd.Msec;
                if (pm.TimeStepSound < 0)
                    pm.TimeStepSound = 0;
            }
            if (pm.DuckTime > 0)
            {
                pm.DuckTime -= pm.Cmd.Msec;
                if (pm.DuckTime < 0)
                    pm.DuckTime = 0;
            }
            if (pm.SwimTime > 0)
            {
                pm.SwimTime -= pm.Cmd.Msec;
                if (pm.SwimTime < 0)
                    pm.SwimTime = 0;
            }
        }

        // ------------------------------------------------------------------
        // Toss and noclip

        MoveTrace PushEntity(Vec3 push)
        {
            Vec3 end = pm.Origin + push;
            MoveTrace trace = Trace(pm.Origin, end);
            pm.Origin = trace.EndPos;

            if (trace.Fraction < 1.0 && !trace.AllSolid)
                AddToTouched(trace);

            return trace;
        }

        /// <summary>Dead player flying through the air (PM_Physics_Toss).</summary>
        void PhysicsToss()
        {
            CheckWater();

            if (pm.Velocity.Z > 0)
                pm.OnGround = -1;

            if (pm.OnGround != -1 && pm.BaseVelocity.IsZero && pm.Velocity.IsZero)
                return;

            CheckVelocity();

            if (pm.MoveType != MoveType.Fly && pm.MoveType != MoveType.BounceMissile && pm.MoveType != MoveType.FlyMissile)
                AddGravity();

            pm.Velocity = pm.Velocity + pm.BaseVelocity;
            CheckVelocity();
            Vec3 move = pm.Velocity * pm.FrameTime;
            pm.Velocity = pm.Velocity - pm.BaseVelocity;

            MoveTrace trace = PushEntity(move);

            CheckVelocity();

            if (trace.AllSolid)
            {
                pm.OnGround = trace.Ent;
                pm.Velocity = Vec3.Zero;
                return;
            }

            if (trace.Fraction == 1)
            {
                CheckWater();
                return;
            }

            float backoff;
            if (pm.MoveType == MoveType.Bounce)
                backoff = 2.0f - pm.Friction;
            else if (pm.MoveType == MoveType.BounceMissile)
                backoff = 2.0f;
            else
                backoff = 1;

            ClipVelocity(pm.Velocity, trace.PlaneNormal, out Vec3 clipped, backoff);
            pm.Velocity = clipped;

            if (trace.PlaneNormal.Z > 0.7)
            {
                if (pm.Velocity.Z < mv.Gravity * pm.FrameTime)
                {
                    // rolling on the ground: add static friction
                    pm.OnGround = trace.Ent;
                    var v = pm.Velocity; v.Z = 0; pm.Velocity = v;
                }

                float vel = Vec3.Dot(pm.Velocity, pm.Velocity);

                if (vel < (30 * 30) || (pm.MoveType != MoveType.Bounce && pm.MoveType != MoveType.BounceMissile))
                {
                    pm.OnGround = trace.Ent;
                    pm.Velocity = Vec3.Zero;
                }
                else
                {
                    move = pm.Velocity * (float)((1.0 - trace.Fraction) * pm.FrameTime * 0.9);
                    PushEntity(move);
                }
            }

            CheckWater();
        }

        void NoClip()
        {
            float fmove = pm.Cmd.ForwardMove;
            float smove = pm.Cmd.SideMove;

            var f = pm.Forward; f.Normalize(); pm.Forward = f;
            var r = pm.Right; r.Normalize(); pm.Right = r;

            Vec3 wishVel = default;
            for (int i = 0; i < 3; i++)
                wishVel[i] = pm.Forward[i] * fmove + pm.Right[i] * smove;
            wishVel.Z += pm.Cmd.UpMove;

            pm.Origin = Vec3.MA(pm.Origin, pm.FrameTime, wishVel);
            pm.Velocity = Vec3.Zero;
        }

        // ------------------------------------------------------------------
        // The move

        void PlayerMove(bool server)
        {
            ILadder ladder = null;

            pm.Server = server;

            CheckParameters();

            Touched.Clear();

            pm.FrameTime = (float)(pm.Cmd.Msec * 0.001);

            ReduceTimers();

            Vec3.AngleVectors(pm.Angles, out Vec3 f, out Vec3 r, out Vec3 u);
            pm.Forward = f; pm.Right = r; pm.Up = u;

            if (pm.Spectator || pm.IUser1 > 0)
            {
                // Spectator movement is not part of the port yet
                CategorizePosition();
                return;
            }

            // Always try to unstick unless noclipping. (The legacy TFC
            // server simply waits; it doesn't try ducking out.)
            if (pm.MoveType != MoveType.Noclip && pm.MoveType != MoveType.None)
            {
                if (CheckStuck())
                    return; // can't move, we're stuck
            }

            CategorizePosition();

            pm.OldWaterLevel = pm.WaterLevel;

            if (pm.OnGround == -1)
                pm.FallVelocity = -pm.Velocity.Z;

            onLadder = false;
            if (!pm.Dead && (pm.Flags & MoveFlags.OnTrain) == 0)
            {
                ladder = world.FindLadder(pm);
                if (ladder != null)
                    onLadder = true;
            }

            // (footstep timing: UpdateStepSound, not part of the physics)

            Duck();

            if (!pm.Dead && (pm.Flags & MoveFlags.OnTrain) == 0)
            {
                if (ladder != null)
                    LadderMove(ladder);
                else if (pm.MoveType != MoveType.Walk && pm.MoveType != MoveType.Noclip)
                    pm.MoveType = MoveType.Walk; // leave the ladder
            }

            switch (pm.MoveType)
            {
                default:
                case MoveType.None:
                    break;

                case MoveType.Noclip:
                    NoClip();
                    break;

                case MoveType.Toss:
                case MoveType.Bounce:
                    PhysicsToss();
                    break;

                case MoveType.Fly:
                    CheckWater();

                    if ((pm.Cmd.Buttons & Buttons.Jump) != 0)
                    {
                        if (ladder == null)
                            Jump();
                    }
                    else
                    {
                        pm.OldButtons &= ~Buttons.Jump;
                    }

                    pm.Velocity = pm.Velocity + pm.BaseVelocity;
                    FlyMove();
                    pm.Velocity = pm.Velocity - pm.BaseVelocity;
                    break;

                case MoveType.Walk:
                    if (!InWater())
                        AddCorrectGravity();

                    // Leaping out of the water
                    if (pm.WaterJumpTime != 0)
                    {
                        WaterJump();
                        FlyMove();
                        CheckWater();
                        return;
                    }

                    if (pm.WaterLevel >= 2)
                    {
                        if (pm.WaterLevel == 2)
                            CheckWaterJump();

                        // Falling again: no longer jumping out of water
                        if (pm.Velocity.Z < 0 && pm.WaterJumpTime != 0)
                            pm.WaterJumpTime = 0;

                        if ((pm.Cmd.Buttons & Buttons.Jump) != 0)
                            Jump();
                        else
                            pm.OldButtons &= ~Buttons.Jump;

                        WaterMove();

                        pm.Velocity = pm.Velocity - pm.BaseVelocity;

                        CategorizePosition();
                    }
                    else
                    {
                        if ((pm.Cmd.Buttons & Buttons.Jump) != 0)
                        {
                            if (ladder == null)
                                Jump();
                        }
                        else
                        {
                            pm.OldButtons &= ~Buttons.Jump;
                        }

                        // Friction before base velocity, so conveyors don't slow you
                        if (pm.OnGround != -1)
                        {
                            var v = pm.Velocity; v.Z = 0.0f; pm.Velocity = v;
                            Friction();
                        }

                        CheckVelocity();

                        if (pm.OnGround != -1)
                            WalkMove();
                        else
                            AirMove();

                        CategorizePosition();

                        pm.Velocity = pm.Velocity - pm.BaseVelocity;

                        CheckVelocity();

                        if (!InWater())
                            FixupGravityVelocity();

                        if (pm.OnGround != -1)
                        {
                            var v = pm.Velocity; v.Z = 0; pm.Velocity = v;
                        }

                        CheckFalling();
                    }

                    PlayWaterSounds();
                    break;
            }
        }

        /// <summary>True if the last move found the player on a ladder.</summary>
        public bool OnLadder => onLadder;
    }
}

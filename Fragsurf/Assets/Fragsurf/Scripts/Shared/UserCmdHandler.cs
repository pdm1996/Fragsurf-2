﻿using System.Collections.Generic;
using Fragsurf.Shared.Entity;
using Fragsurf.Shared.Packets;
using Fragsurf.Shared.Player;

namespace Fragsurf.Shared
{
    public class UserCmdHandler : FSSharedScript
    {

        // TODO: Consider moving hovered entity logic to Human's InputController.

        private Dictionary<IPlayer, UserCmd> _finalCmds = new Dictionary<IPlayer, UserCmd>(256);
        private Dictionary<int, int> _hoveredEnts = new Dictionary<int, int>();

        public NetEntity GetHoveredEntity(int clientIndex)
        {
            if (!_hoveredEnts.ContainsKey(clientIndex))
            {
                return null;
            }
            return Game.EntityManager.FindEntity(_hoveredEnts[clientIndex]);
        }

        public void HandleUserCommand(IPlayer player, UserCmd cmd, bool isPrediction = false)
        {
            HandleRunCommand(player, cmd, isPrediction);

            if (Game.IsHost || isPrediction)
            {
                HandleInteractables(player, cmd);
            }
        }

        protected override void OnPlayerPacketReceived(IPlayer player, IBasePacket packet)
        {
            if(packet is UserCmd cmd)
            {
                HandleUserCommand(player, cmd);
            }
        }

        protected override void OnPlayerDisconnected(IPlayer player)
        {
            if (_hoveredEnts.ContainsKey(player.ClientIndex))
            {
                _hoveredEnts.Remove(player.ClientIndex);
            }

            if (!Game.IsHost)
            {
                return;
            }

            if (_finalCmds.ContainsKey(player))
            {
                _finalCmds.Remove(player);
            }
        }

        protected override void _Tick()
        {
            if (!Game.IsHost)
            {
                return;
            }

            foreach(var kvp in _finalCmds)
            {
                Game.Network.BroadcastPacket(kvp.Value);
            }

            _finalCmds.Clear();
        }

        private void HandleInteractables(IPlayer player, UserCmd cmd)
        {
            var ent = cmd.HoveredEntity > 0 
                ? Game.EntityManager.FindEntity(cmd.HoveredEntity) as IInteractable
                : null;

            if(_hoveredEnts.ContainsKey(player.ClientIndex)
                && _hoveredEnts[player.ClientIndex] != cmd.HoveredEntity)
            {
                var hoveredEnt = Game.EntityManager.FindEntity(_hoveredEnts[player.ClientIndex]);
                if (hoveredEnt != null
                    && hoveredEnt is IInteractable interactable)
                {
                    interactable.MouseExit(player.ClientIndex);
                }
                _hoveredEnts.Remove(player.ClientIndex);
            }
            else if(ent != null)
            {
                if(_hoveredEnts.ContainsKey(player.ClientIndex)
                    && _hoveredEnts[player.ClientIndex] == cmd.HoveredEntity)
                {
                    return;
                }
                _hoveredEnts[player.ClientIndex] = cmd.HoveredEntity;
                ent.MouseEnter(player.ClientIndex);
            }
        }

        private void HandleRunCommand(IPlayer player, UserCmd cmd, bool isPrediction = false)
        {
            if (!(player.Entity is Human human))
            {
                return;
            }

            human.RunCommand(cmd, isPrediction);

            if(Game.IsHost || isPrediction)
            {
                Game.PlayerManager.RaiseRunCommand(player);
            }

            if (Game.IsHost && !isPrediction)
            {
                UserCmd newCmd;
                if (_finalCmds.ContainsKey(player))
                {
                    newCmd = _finalCmds[player];
                }
                else
                {
                    newCmd = PacketUtility.TakePacket<UserCmd>();
                    _finalCmds[player] = newCmd;
                }
                newCmd.Copy(cmd);
                newCmd.ClientIndex = player.ClientIndex;
                newCmd.Origin = human.Origin;
                newCmd.Velocity = human.Velocity;
                newCmd.BaseVelocity = human.BaseVelocity;
                // usercmd is ReliableSequenced by default so clients don't lose inputs
                // but we don't want the host to clog up with this shit so switch to to UnreliableSequenced just to send it back to the client
                newCmd.Sc = SendCategory.Gameplay;

                // broadcast
                //ServerLoop.Instance.BetterGameServer.BroadcastPacketEnqueue(newCmd);
            }
            else if(!Game.IsHost && isPrediction)
            {
                cmd.Origin = human.Origin;
                cmd.Velocity = human.Velocity;
                cmd.BaseVelocity = human.BaseVelocity;

                var cmdToSend = PacketUtility.TakePacket<UserCmd>();
                cmdToSend.Copy(cmd);
                Game.Network.BroadcastPacket(cmdToSend);
            }
        }

    }
}


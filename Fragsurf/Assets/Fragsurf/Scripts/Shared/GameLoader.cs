﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Fragsurf.Shared.Maps;
using Fragsurf.Shared.Packets;
using Fragsurf.Client;
using Fragsurf.Server;
using UnityEngine.SceneManagement;
using Steamworks;
using Steamworks.Ugc;
using UnityEngine;
using Fragsurf.UI;

namespace Fragsurf.Shared
{
    public enum GameLoadResult
    {
        None,
        Cancelled,
        FailedToConnect,
        FailedToSync,
        MissingMapChange,
        FailedToLoadMap,
        FailedToLoadGamemode,
        MissingBackfill,
        Success
    }

    public enum GameLoaderState 
    { 
        None,
        Idle,
        Playing,
        Creating,
        Joining,
        Unloading,
        ChangingMap
    }

    // todo: rewrite game join & creation process so it's not such a clusterfuck and hard to work with
    public class GameLoader : FSComponent
    {
        public event Action PreGameLoaded;
        public event Action GameLoaded;
        public event Action PreGameUnloaded;
        public event Action GameUnloaded;

        public bool Loading;
        public string LoadingHint;
        private bool _cancelled;
        private CancellationTokenSource _cts;

        public GameLoaderState State { get; private set; }

        protected override void _Start()
        {
            if(!Game.IsHost)
            {
                (Game.Network as ClientSocketManager).OnStatusChanged += Socket_OnStatusChanged;
            }
            else
            {
                DevConsole.RegisterCommand("map.change", "", this, (e) =>
                {
                    if (e.Length > 1)
                    {
                        var map = e[1];
                        string gamemode = Game.GamemodeLoader.Gamemode.Name;
                        if (e.Length > 2)
                        {
                            gamemode = e[2];
                        }
                        var name = DevConsole.GetVariable<string>("server.name");
                        var pass = DevConsole.GetVariable<string>("server.password");
                        ChangeMapAsync(map, gamemode, name, pass);
                    }
                });
            }
        }

        public void ChangeMap(string mapName)
        {
            string gamemode = Game.GamemodeLoader.Gamemode.Name;
            var name = DevConsole.GetVariable<string>("server.name");
            var pass = DevConsole.GetVariable<string>("server.password");
            ChangeMapAsync(mapName, gamemode, name, pass);
        }

        private async void ChangeMapAsync(string map, string gamemode, string name, string pass)
        {
            if(State != GameLoaderState.Playing)
            {
                Debug.LogError($"{Game.IsHost} Can't change map while state is: {State}");
                return;
            }

            State = GameLoaderState.ChangingMap;

            GameServer.Instance.Socket.DisconnectAllPlayers(DenyReason.MapChange.ToString());
            await Task.Delay(100);
            await CreateGameAsync(map, gamemode);
        }

        private void Socket_OnStatusChanged(ClientSocketStatus status, string reason = null)
        {
            if (Game.IsHost)
            {
                return;
            }

            if(status == ClientSocketStatus.Disconnected)
            {
                if (reason == DenyReason.MapChange.ToString())
                {
                    BeginRetry();
                }
            }
        }

        private bool _retrying;
        private async void BeginRetry()
        {
            if (_retrying)
            {
                return;
            }
            _retrying = true;
            var attempts = 8;
            var delay = 1500;
            while (attempts > 0 && _retrying)
            {
                attempts--;
                await JoinGameAsync(_lastAddress, _lastPort, _lastPassword);
                await Task.Delay(delay);
            }
            _retrying = false;
        }

        public void Cancel()
        {
            _cancelled = true;
            _retrying = false;
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }
        }

        protected override void _Destroy()
        {
            PreGameLoaded = null;
            GameLoaded = null;
            PreGameUnloaded = null;
            GameUnloaded = null;
            State = GameLoaderState.None;
        }

        private string _lastAddress;
        private int _lastPort;
        private string _lastPassword;
        public async Task<GameLoadResult> JoinGameAsync(string address, int port = 0, string password = null)
        {
            if(State != GameLoaderState.None)
            {
                Debug.LogError($"{Game.IsHost} Can't join while state is: {State}");
                return GameLoadResult.None;
            }

            State = GameLoaderState.Joining;
            _lastAddress = address;
            _lastPort = port;
            _lastPassword = password;
            _cancelled = false;

            FileSystem.EmptyTempFolder();

            Loading = true;
            var result = await _JoinGameAsync(address, port, password);
            Loading = false;

            if (result != GameLoadResult.Success)
            {
                return result;
            }
            else
            {
                _retrying = false;
            }

            State = GameLoaderState.Playing;

            return result;
        }

        private async Task<GameLoadResult> _JoinGameAsync(string address, int port = 0, string password = null)
        {
            if(Game.IsHost)
            {
                return GameLoadResult.None;
            }

            // 1. Connect
            LoadingHint = "Connecting to host";
            if (_cancelled) { return GameLoadResult.Cancelled; }

            var connectionResult = await (Game.Network as ClientSocketManager).ConnectAsync(address, port, password);
            if(connectionResult != ClientSocketStatus.Connected)
            {
                return GameLoadResult.FailedToConnect;
            }

            // 2. Sync files
            //LoadingHint = "Synchronizing files";
            //if (_cancelled) { return GameLoadResult.Cancelled; }

            //_cts?.Dispose();
            //_cts = new CancellationTokenSource();
            //var syncResult = await Game.GetFSComponent<FileDownloader>().SyncWithHostAsync(_cts);
            //_cts?.Dispose();
            //if (syncResult != FileDownloader.SyncState.Completed)
            //{
            //    return GameLoadResult.FailedToSync;
            //}

            // 3. Request ClientIndex and what map, gamemode to load
            LoadingHint = "Requesting game info";
            if (_cancelled) { return GameLoadResult.Cancelled; }

            Game.Network.BroadcastPacket(PacketUtility.TakePacket<MapChange>());
            var mapChange = await (Game.Network as ClientSocketManager).WaitForPacketAsync<MapChange>(5000);
            if(mapChange == null)
            {
                return GameLoadResult.MissingMapChange;
            }

            Game.ClientIndex = mapChange.ClientIndex;
            State = GameLoaderState.ChangingMap;

            // 5. Load the map
            LoadingHint = "Loading the map: " + mapChange.MapName;
            if (_cancelled) { return GameLoadResult.Cancelled; }

            // with a local server the map should already be loaded, so let's check first
            if (MapLoader.Instance.CurrentMap == null || MapLoader.Instance.CurrentMap.Name != mapChange.MapName)
            {
                var mapLoadResult = await MapLoader.Instance.LoadMapAsync2(mapChange.MapName);
                if (mapLoadResult != MapLoadState.Loaded)
                {
                    return GameLoadResult.FailedToLoadMap;
                }
            }

            PreGameLoaded?.Invoke();

            // 6. Load the gamemode
            LoadingHint = "Loading the gamemode: " + mapChange.Gamemode;
            if (_cancelled) { return GameLoadResult.Cancelled; }

            var gamemodeLoaded = Game.GamemodeLoader.LoadGamemode(mapChange.Gamemode);
            if(!gamemodeLoaded)
            {
                return GameLoadResult.FailedToLoadGamemode;
            }

            GameLoaded?.Invoke();

            // 7. Notify server we're in
            LoadingHint = "Done, entering game";
            if (_cancelled) { return GameLoadResult.Cancelled; }

            var pi2 = PacketUtility.TakePacket<PlayerIntroduction>();
            pi2.Step = PlayerIntroduction.JoinStep.Introduce;
            Game.Network.BroadcastPacket(pi2);

            State = GameLoaderState.Playing;

            return GameLoadResult.Success;
        }

        public async Task<GameLoadResult> CreateServerAsync(string mapName, string gamemode, string name = null, string pass = null)
        {
            if(GameServer.Instance != null)
            {
                GameServer.Instance.Destroy();
                await Task.Delay(100);
            }

            Loading = true;
            LoadingHint = "Creating server";

            var obj = new GameObject("[Server]");
            var server = obj.AddComponent<GameServer>();
            server.IsLocalServer = true;

            if (!string.IsNullOrEmpty(name))
            {
                DevConsole.ExecuteLine("server.name \"" + name + "\"");
            }
            if (!string.IsNullOrEmpty(pass))
            {
                DevConsole.ExecuteLine("server.password \"" + pass + "\"");
            }

            var result = await server.GameLoader.CreateGameAsync(mapName, gamemode);
            if(result != GameLoadResult.Success)
            {
                Debug.LogError("Fucked: " + result);
                server.Destroy();
                if (!Game.IsHost)
                {
                    SceneManager.LoadScene(GameData.Instance.MainMenu.ScenePath);
                    UGuiManager.Instance.Popup("Couldn't load that map, something went wrong.");
                }
                Loading = false;
            }

            return result;
        }

        public async Task<GameLoadResult> CreateGameAsync(string mapName, string gamemode)
        {
            if (State != GameLoaderState.None)
            {
                Debug.LogError($"{Game.IsHost} Can't create game while state is: {State}");
                return GameLoadResult.None;
            }

            State = GameLoaderState.Creating;

            FileSystem.ClearDownloadList();

            _cancelled = false;
            Loading = true;
            var result = await _CreateGameAsync(mapName, gamemode);
            Loading = false;

            if(result == GameLoadResult.Success)
            {
                State = GameLoaderState.Playing;
            }

            return result;
        }

        private async Task<GameLoadResult> _CreateGameAsync(string mapName, string gamemode)
        {
            DevConsole.SetVariable("game.mode", gamemode, true, true);

            State = GameLoaderState.ChangingMap;

            LoadingHint = "Loading map: " + mapName;

            if(ulong.TryParse(mapName, out ulong workshopId))
            {
                LoadingHint = "Downloading map from workshop";

                if(!SteamServer.IsValid
                    && !SteamClient.IsValid)
                {
                    return GameLoadResult.FailedToLoadMap;
                }

                var item = await Item.GetAsync(workshopId);
                if (!item.HasValue)
                {
                    return GameLoadResult.FailedToLoadMap;
                }

                var download = await SteamUGC.DownloadAsync(workshopId);
                if(!download)
                {
                    return GameLoadResult.FailedToLoadMap;
                }

                var provider = FileSystem.AddLocalProvider(item.Value.Directory);
                FileSystem.Build();
                foreach(var file in provider.Files)
                {
                    file.Value.WorkshopId = workshopId;
                }
            }

            if (MapLoader.Instance.CurrentMap == null || MapLoader.Instance.CurrentMap.Name != mapName)
            {
                var mapLoadResult = await MapLoader.Instance.LoadMapAsync2(mapName);
                if (mapLoadResult != MapLoadState.Loaded)
                {
                    return GameLoadResult.FailedToLoadMap;
                }
            }

            if (_cancelled) 
            {
                return GameLoadResult.Cancelled; 
            }

            PreGameLoaded?.Invoke();

            LoadingHint = "Loading gamemode: " + gamemode;

            var gamemodeLoaded = Game.GamemodeLoader.LoadGamemode(gamemode);
            if (!gamemodeLoaded)
            {
                return GameLoadResult.FailedToLoadGamemode;
            }
            GameLoaded?.Invoke();

            State = GameLoaderState.Playing;

            LoadingHint = "Done, game is created.";

            return GameLoadResult.Success;
        }

    }
}

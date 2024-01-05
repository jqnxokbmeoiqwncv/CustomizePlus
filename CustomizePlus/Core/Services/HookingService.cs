﻿using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using System;
using System.Runtime.InteropServices;
using OtterGui.Log;
using CustomizePlus.Core.Data;
using CustomizePlus.Game.Services;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Profiles;
using CustomizePlus.Armatures.Services;
using CustomizePlus.GameData.Data;

namespace CustomizePlus.Core.Services;

public class HookingService : IDisposable
{
    private readonly PluginConfiguration _configuration;
    private readonly ISigScanner _sigScanner;
    private readonly IGameInteropProvider _hooker;
    private readonly ProfileManager _profileManager;
    private readonly ArmatureManager _armatureManager;
    private readonly GameStateService _gameStateService;
    private readonly FantasiaPlusDetectService _fantasiaPlusDetectService;
    private readonly Logger _logger;

    private Hook<RenderDelegate>? _renderManagerHook;
    private Hook<GameObjectMovementDelegate>? _gameObjectMovementHook;

    private delegate nint RenderDelegate(nint a1, nint a2, int a3, int a4);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GameObjectMovementDelegate(nint gameObject);

    public HookingService(
        PluginConfiguration configuration,
        ISigScanner sigScanner,
        IGameInteropProvider hooker,
        ProfileManager profileManager,
        ArmatureManager armatureManager,
        GameStateService gameStateService,
        FantasiaPlusDetectService fantasiaPlusDetectService,
        Logger logger)
    {
        _configuration = configuration;
        _sigScanner = sigScanner;
        _hooker = hooker;
        _profileManager = profileManager;
        _armatureManager = armatureManager;
        _gameStateService = gameStateService;
        _fantasiaPlusDetectService = fantasiaPlusDetectService;
        _logger = logger;

        ReloadHooks();
    }

    public void ReloadHooks()
    {
        try
        {
            if (_configuration.PluginEnabled)
            {
                if (_renderManagerHook == null)
                {
                    var renderAddress = _sigScanner.ScanText(Constants.RenderHookAddress);
                    _renderManagerHook = _hooker.HookFromAddress<RenderDelegate>(renderAddress, OnRender);
                    _logger.Debug("Render hook established");
                }

                if (_gameObjectMovementHook == null)
                {
                    var movementAddress = _sigScanner.ScanText(Constants.MovementHookAddress);
                    _gameObjectMovementHook = _hooker.HookFromAddress<GameObjectMovementDelegate>(movementAddress, OnGameObjectMove);
                    _logger.Debug("Movement hook established");
                }

                _logger.Debug("Hooking render & movement functions");
                _renderManagerHook.Enable();
                _gameObjectMovementHook.Enable();

                _logger.Debug("Hooking render manager");
                _renderManagerHook.Enable();

                //Send current player's profile update message to IPC
                //IPCManager.OnProfileUpdate(null);
            }
            else
            {
                _logger.Debug("Unhooking...");
                _renderManagerHook?.Disable();
                _gameObjectMovementHook?.Disable();
                _renderManagerHook?.Disable();
            }
        }
        catch (Exception e)
        {
            _logger.Error($"Failed to hook Render::Manager::Render {e}");
            throw;
        }
    }

    private nint OnRender(nint a1, nint a2, int a3, int a4)
    {
        if (_renderManagerHook == null)
        {
            throw new Exception();
        }

        if (_fantasiaPlusDetectService.IsFantasiaPlusInstalled)
        {
            _logger.Error($"Fantasia+ detected, disabling all hooks");
            _renderManagerHook?.Disable();
            _gameObjectMovementHook.Disable();

            return _renderManagerHook.Original(a1, a2, a3, a4);
        }

        try
        {
            _armatureManager.OnRender();
            _profileManager.OnRender();
        }
        catch (Exception e)
        {
            _logger.Error($"Error in Customize+ render hook {e}");
            _renderManagerHook?.Disable();
        }

        return _renderManagerHook.Original(a1, a2, a3, a4);
    }

    private unsafe void OnGameObjectMove(nint gameObjectPtr)
    {
        // Call the original function.
        _gameObjectMovementHook.Original(gameObjectPtr);

        // If GPose and a 3rd-party posing service are active simultneously, abort
        if (_gameStateService.GameInPosingMode())
        {
            return;
        }

        var actor = (Actor)gameObjectPtr;
        if (actor.Valid)
            _armatureManager.OnGameObjectMove((Actor)gameObjectPtr);
    }

    public void Dispose()
    {
        _gameObjectMovementHook?.Disable();
        _gameObjectMovementHook?.Dispose();

        _renderManagerHook?.Disable();
        _renderManagerHook?.Dispose();
    }
}
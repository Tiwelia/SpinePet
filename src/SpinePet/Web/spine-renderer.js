(function () {
  "use strict";

  var scene = document.createElement("div");
  scene.id = "scene";
  scene.style.cssText = "width:100%;height:100%;position:relative;";
  document.body.appendChild(scene);

  var app = new PIXI.Application({
    resizeTo: scene,
    antialias: true,
    autoDensity: true,
    transparent: true,
    resolution: window.devicePixelRatio || 1,
  });
  scene.appendChild(app.view);

  var MAX_ALLOWED_SCALE = 2.0;
  var MIN_SCALE = 0.05;
  var HIT_MASK_MAX_SIZE = 192;
  var ACTION_CLICK_COOLDOWN_MS = 180;
  var characterStates = {};
  var resourceCache = {};
  var configMode = true;
  var characterDragActive = false;
  var hitRegionsFrame = 0;
  var hitRegionsPaused = false;
  var hitMaskQueue = [];
  var hitMaskQueueScheduled = false;

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", handleMsg);
  }

  function send(type, data) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(JSON.stringify({ type: type, data: data || {} }));
    }
  }

  function renderClearFrame(requestId) {
    app.render();
    requestAnimationFrame(function () {
      app.render();
      requestAnimationFrame(function () {
        app.render();
        send("frameCleared", { requestId: requestId || 0 });
      });
    });
  }

  function scheduleHitRegionsChanged() {
    if (hitRegionsPaused) return;
    if (hitRegionsFrame) return;
    hitRegionsFrame = requestAnimationFrame(function () {
      hitRegionsFrame = 0;
      sendHitRegionsChanged();
    });
  }

  function sendHitRegionsChanged() {
    if (hitRegionsPaused) return;
    var regions = [];
    Object.keys(characterStates).forEach(function (id) {
      var state = characterStates[id];
      if (!state || !state.visible || !state.spine) return;

      try {
        var bounds = state.spine.getBounds(false);
        if (!bounds || bounds.width <= 0 || bounds.height <= 0) return;
        regions.push({
          id: id,
          x: bounds.x,
          y: bounds.y,
          width: bounds.width,
          height: bounds.height
        });
      } catch (error) {
        console.warn("[SpinePet] hit-region calculation failed:", error);
      }
    });
    send("hitRegionsChanged", { regions: regions });
  }

  function log(msg) {
    console.log("[SpinePet]", msg);
    send("log", { message: msg });
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  function updateCanvasCursor() {
    app.view.style.cursor = configMode
      ? "default"
      : (characterDragActive ? "grabbing" : "grab");
  }

  function scheduleHitMask(state) {
    if (!state || !state.spine || state.hitMaskQueued) return;
    state.hitMask = null;
    state.hitMaskQueued = true;
    hitMaskQueue.push(state);
    scheduleNextHitMask();
  }

  function scheduleNextHitMask() {
    if (hitMaskQueueScheduled || hitMaskQueue.length === 0) return;
    hitMaskQueueScheduled = true;

    var scheduleIdle = window.requestIdleCallback || function (callback) {
      return setTimeout(callback, 32);
    };
    scheduleIdle(function () {
      hitMaskQueueScheduled = false;
      var state = hitMaskQueue.shift();
      if (state) {
        state.hitMaskQueued = false;
        buildHitMask(state);
      }
      if (hitMaskQueue.length > 0) {
        setTimeout(scheduleNextHitMask, 16);
      }
    }, { timeout: 500 });
  }

  function buildHitMask(state) {
    if (!state || !state.spine) return;
    var extract = app.renderer && app.renderer.plugins && app.renderer.plugins.extract;
    if (!extract || !extract.canvas) return;

    try {
      var sourceCanvas = extract.canvas(state.spine);
      if (!sourceCanvas || !sourceCanvas.width || !sourceCanvas.height) return;

      var ratio = Math.min(
        1,
        HIT_MASK_MAX_SIZE / Math.max(sourceCanvas.width, sourceCanvas.height)
      );
      var width = Math.max(1, Math.round(sourceCanvas.width * ratio));
      var height = Math.max(1, Math.round(sourceCanvas.height * ratio));
      var maskCanvas = document.createElement("canvas");
      maskCanvas.width = width;
      maskCanvas.height = height;
      var ctx = maskCanvas.getContext("2d", { willReadFrequently: true });
      if (!ctx) return;

      ctx.clearRect(0, 0, width, height);
      ctx.drawImage(sourceCanvas, 0, 0, width, height);
      var rgba = ctx.getImageData(0, 0, width, height).data;
      var alpha = new Uint8Array(width * height);
      for (var i = 0, p = 3; i < alpha.length; i++, p += 4) {
        alpha[i] = rgba[p];
      }

      state.hitMask = { width: width, height: height, alpha: alpha };
    } catch (error) {
      console.warn("[SpinePet] hit-mask generation failed:", error);
      state.hitMask = null;
    }
  }

  function normalizeBounds(bounds) {
    var width = bounds && isFinite(bounds.width) && bounds.width > 1 ? bounds.width : app.screen.width;
    var height = bounds && isFinite(bounds.height) && bounds.height > 1 ? bounds.height : app.screen.height;
    var x = bounds && isFinite(bounds.x) ? bounds.x : -width / 2;
    var y = bounds && isFinite(bounds.y) ? bounds.y : -height;
    return { x: x, y: y, width: width, height: height };
  }

  function getState(id) {
    if (!characterStates[id]) {
      characterStates[id] = {
        id: id,
        spine: null,
        bounds: null,
        scale: 0.2,
        maxScale: MAX_ALLOWED_SCALE,
        speed: 1.0,
        animation: "",
        animations: [],
        x: 200,
        y: 200,
        visible: false,
        loadSeq: 0,
        timer: 0,
        hitMask: null,
        hitMaskQueued: false,
        lastActionAt: 0,
      };
    }
    return characterStates[id];
  }

  function clearLoadTimer(state) {
    if (state.timer) {
      clearTimeout(state.timer);
      state.timer = 0;
    }
  }

  function getResourceCacheKey(skelUrl, atlasUrl) {
    return skelUrl + "|" + atlasUrl;
  }

  function relayoutState(state) {
    if (!state || !state.spine || !state.bounds) return;
    state.spine.pivot.set(
      state.bounds.x + state.bounds.width / 2,
      state.bounds.y + state.bounds.height
    );
    state.spine.position.set(state.x, state.y);
    state.spine.scale.set(clamp(state.scale, MIN_SCALE, state.maxScale));
    state.spine.visible = !!state.visible;
    scheduleHitRegionsChanged();
    log("layout id=" + state.id + " x=" + state.x + " y=" + state.y +
      " scale=" + state.scale + " visible=" + state.visible +
      " bounds=" + JSON.stringify(state.bounds));
  }

  function relayoutAll() {
    Object.keys(characterStates).forEach(function (id) {
      relayoutState(characterStates[id]);
    });
  }

  function moveStatePosition(state, x, y) {
    if (!state || !isFinite(x) || !isFinite(y)) return;
    state.x = x;
    state.y = y;
    if (state.spine) {
      state.spine.position.set(x, y);
    }
  }

  function setHitRegionsPaused(paused) {
    hitRegionsPaused = !!paused;
    if (hitRegionsPaused && hitRegionsFrame) {
      cancelAnimationFrame(hitRegionsFrame);
      hitRegionsFrame = 0;
    }
    if (!hitRegionsPaused) {
      scheduleHitRegionsChanged();
    }
  }

  function destroySpine(state) {
    if (!state || !state.spine) return;
    app.stage.removeChild(state.spine);
    try {
      state.spine.destroy();
    } catch (error) {
      console.warn("[SpinePet] Spine cleanup failed:", error);
    }
    state.spine = null;
    state.bounds = null;
    state.animations = [];
    state.animation = "";
    state.hitMask = null;
    state.hitMaskQueued = false;
    scheduleHitRegionsChanged();
  }

  function sendCharacterError(id, message) {
    send("characterError", { id: id, message: message });
  }

  function sendCharacterLoaded(state) {
    send("characterLoaded", {
      id: state.id,
      animations: state.animations,
      maxScale: state.maxScale,
      appliedScale: state.scale
    });
  }

  function sendCharacterScaleChanged(state) {
    send("characterScaleChanged", {
      id: state.id,
      scale: state.scale,
      maxScale: state.maxScale
    });
  }

  function buildSpineFromResource(state, spineData) {
    destroySpine(state);

    var spine = new PIXI.spine.Spine(spineData);
    spine.autoUpdate = false;
    app.stage.addChild(spine);
    spine.update(0);
    state.spine = spine;
    state.bounds = normalizeBounds(spine.getLocalBounds());
    state.maxScale = MAX_ALLOWED_SCALE;
    state.scale = clamp(state.scale, MIN_SCALE, state.maxScale);
    state.animations = [];
    var anims = spineData.animations;
    if (anims) {
      for (var i = 0; i < anims.length; i++) state.animations.push(anims[i].name);
    }

    log("build-spine id=" + state.id + " anims=" + state.animations.length +
      " bounds=" + JSON.stringify(state.bounds));

    relayoutState(state);
    if (state.spine.state) {
      state.spine.state.timeScale = state.speed;
      if (!configMode && state.animations.indexOf("idle") >= 0) {
        state.animation = "idle";
        state.spine.state.setAnimation(0, "idle", true);
      } else if (state.animation && state.animations.indexOf(state.animation) >= 0) {
        state.spine.state.setAnimation(0, state.animation, true);
      } else if (state.animations.indexOf("idle") >= 0) {
        state.animation = "idle";
        state.spine.state.setAnimation(0, "idle", true);
      } else if (state.animations.length > 0) {
        state.animation = state.animations[0];
        state.spine.state.setAnimation(0, state.animation, true);
      }
    }
    scheduleHitMask(state);
  }

  function loadCharacter(id, skelUrl, atlasUrl, timeoutMs) {
    var state = getState(id);
    var loadSeq = ++state.loadSeq;
    var resourceKey = "spineData_" + id + "_" + loadSeq;
    var cacheKey = getResourceCacheKey(skelUrl, atlasUrl);

    clearLoadTimer(state);
    log("load:start id=" + id + " seq=" + loadSeq + " skel=" + skelUrl + " atlas=" + atlasUrl);

    if (resourceCache[cacheKey] && resourceCache[cacheKey].spineData) {
      try {
        buildSpineFromResource(state, resourceCache[cacheKey].spineData);
        sendCharacterLoaded(state);
        return;
      } catch (cacheErr) {
        log("load:cache-error id=" + id + " seq=" + loadSeq + " message=" + cacheErr.message);
        delete resourceCache[cacheKey];
      }
    }

    state.timer = setTimeout(function () {
      if (loadSeq !== state.loadSeq) return;
      sendCharacterError(id, "Load timeout");
    }, Math.max(2000, timeoutMs || 8000));

    try {
      var loader = new PIXI.Loader();
      log("load:queue id=" + id + " seq=" + loadSeq);
      loader.add(resourceKey, skelUrl, {
        xhrType: "arraybuffer",
        metadata: {
          spineAtlasFile: atlasUrl
        }
      });

      loader.load(function (_loader, resources) {
        clearLoadTimer(state);
        if (loadSeq !== state.loadSeq) return;

        try {
          var resource = resources[resourceKey];
          if (!resource)
            throw new Error("Spine resource missing from loader results");
          if (resource.error)
            throw resource.error;
          if (!resource.spineData)
            throw new Error("spineData missing after loader parse");

          log("load:done id=" + id + " seq=" + loadSeq + " hasSpineData=" + !!resource.spineData);

          resourceCache[cacheKey] = {
            spineData: resource.spineData
          };

          buildSpineFromResource(state, resource.spineData);
          sendCharacterLoaded(state);
        } catch (loadErr) {
          console.error("[SpinePet] Error:", loadErr);
          log("load:error id=" + id + " seq=" + loadSeq + " message=" + loadErr.message);
          sendCharacterError(id, loadErr.message || String(loadErr));
        }
      });
    } catch (err) {
      clearLoadTimer(state);
      console.error("[SpinePet] Error:", err);
      log("load:error id=" + id + " seq=" + loadSeq + " message=" + err.message);
      sendCharacterError(id, err.message || String(err));
    }
  }

  function handleMsg(e) {
    try {
      var m = JSON.parse(e.data);
      switch (m.type) {
        case "showCharacter":
          var showData = m.data || {};
          if (typeof showData.configMode === "boolean") {
            configMode = showData.configMode;
            updateCanvasCursor();
          }
          var state = getState(showData.id);
          state.x = showData.x || 0;
          state.y = showData.y || 0;
          state.scale = showData.scale || state.scale;
          state.speed = showData.speed || state.speed;
          state.animation = showData.animation || state.animation;
          state.visible = true;
          log("msg:showCharacter id=" + showData.id + " x=" + state.x + " y=" + state.y +
            " scale=" + state.scale + " speed=" + state.speed + " animation=" + state.animation);
          loadCharacter(showData.id, showData.skel || "", showData.atlas || "", showData.timeoutMs);
          break;
        case "hideCharacter":
          var hideState = characterStates[(m.data || {}).id];
          if (hideState) {
            hideState.visible = false;
            relayoutState(hideState);
          }
          break;
        case "clearFrame":
          renderClearFrame((m.data || {}).requestId);
          break;
        case "removeCharacter":
          var removeId = (m.data || {}).id;
          var removeState = characterStates[removeId];
          if (removeState) {
            destroySpine(removeState);
            clearLoadTimer(removeState);
            delete characterStates[removeId];
          }
          break;
        case "hideAllCharacters":
          Object.keys(characterStates).forEach(function (id) {
            characterStates[id].visible = false;
            relayoutState(characterStates[id]);
          });
          break;
        case "moveCharacter":
          var moveData = m.data || {};
          var moveState = characterStates[moveData.id];
          if (moveState) {
            moveStatePosition(moveState, Number(moveData.x), Number(moveData.y));
          }
          break;
        case "playCharacterAnimation":
          playAnim((m.data || {}).id, (m.data || {}).name, (m.data || {}).loop);
          break;
        case "setCharacterScale":
          setScale((m.data || {}).id, (m.data || {}).scale);
          break;
        case "setCharacterSpeed":
          setSpeed((m.data || {}).id, (m.data || {}).speed);
          break;
        case "setConfigMode":
          var modeData = m.data || {};
          if (typeof modeData.configMode === "boolean") {
            configMode = modeData.configMode;
            characterDragActive = false;
            updateCanvasCursor();
            applyConfigModeAnimations(modeData.characters || []);
          }
          scheduleHitRegionsChanged();
          break;
        case "characterPointerDown":
          var pointerData = m.data || {};
          resolveCharacterPointerTarget(
            Number(pointerData.sessionId),
            Number(pointerData.x),
            Number(pointerData.y)
          );
          break;
        case "characterClick":
          playCharacterClick((m.data || {}).id);
          break;
        case "beginCharacterDrag":
          characterDragActive = true;
          updateCanvasCursor();
          setHitRegionsPaused(true);
          playRandomDragAnimation((m.data || {}).id);
          break;
        case "endCharacterDrag":
          endCharacterDrag((m.data || {}).id);
          break;
        case "resize":
          app.renderer.resize(window.innerWidth, window.innerHeight);
          log("msg:resize width=" + window.innerWidth + " height=" + window.innerHeight);
          relayoutAll();
          app.render();
          break;
      }
    } catch (err) { console.error("[SpinePet] msg error:", err); }
  }

  app.view.addEventListener("mousedown", function (e) {
    if (configMode || e.button !== 0) return;

    e.preventDefault();
    var target = findCharacterAtClientPoint(e.clientX, e.clientY);
    if (target) playCharacterClick(target.id);
  });

  function resolveCharacterPointerTarget(sessionId, clientX, clientY) {
    if (configMode ||
        !isFinite(sessionId) ||
        !isFinite(clientX) ||
        !isFinite(clientY)) {
      return;
    }
    var target = findCharacterAtClientPoint(clientX, clientY);
    send("characterPointerTarget", {
      sessionId: sessionId,
      id: target ? target.id : ""
    });
  }

  function findCharacterAtClientPoint(clientX, clientY) {
    var rect = app.view.getBoundingClientRect();
    if (!rect.width || !rect.height) return null;

    var x = (clientX - rect.left) * app.screen.width / rect.width;
    var y = (clientY - rect.top) * app.screen.height / rect.height;
    var candidates = [];

    Object.keys(characterStates).forEach(function (id) {
      var state = characterStates[id];
      if (!state || !state.visible || !state.spine) return;

      try {
        var bounds = state.spine.getBounds(false);
        if (x < bounds.x || y < bounds.y || x > bounds.x + bounds.width || y > bounds.y + bounds.height) return;
        candidates.push({ state: state, bounds: bounds, index: app.stage.getChildIndex(state.spine) });
      } catch (error) {
        console.warn("[SpinePet] character bounds calculation failed:", error);
      }
    });

    candidates.sort(function (a, b) { return b.index - a.index; });
    for (var i = 0; i < candidates.length; i++) {
      if (isOpaqueAt(candidates[i].state, candidates[i].bounds, x, y)) {
        return candidates[i].state;
      }
    }

    return null;
  }

  function isOpaqueAt(state, bounds, x, y) {
    var mask = state && state.hitMask;
    if (!mask || !mask.alpha || !bounds.width || !bounds.height) return true;

    var px = Math.floor((x - bounds.x) * mask.width / bounds.width);
    var py = Math.floor((y - bounds.y) * mask.height / bounds.height);
    for (var oy = -1; oy <= 1; oy++) {
      for (var ox = -1; ox <= 1; ox++) {
        var sx = clamp(px + ox, 0, mask.width - 1);
        var sy = clamp(py + oy, 0, mask.height - 1);
        if (mask.alpha[sy * mask.width + sx] > 16) return true;
      }
    }

    return false;
  }

  function applyConfigModeAnimations(configAnimations) {
    var animationsById = {};
    for (var i = 0; i < configAnimations.length; i++) {
      var item = configAnimations[i] || {};
      animationsById[item.id] = item.animation || "";
    }

    Object.keys(characterStates).forEach(function (id) {
      var state = characterStates[id];
      if (!state || !state.spine) return;

      if (configMode) {
        playAnim(id, animationsById[id], true, true);
      } else if (state.visible) {
        playIdle(state, true);
        scheduleHitMask(state);
      }
    });
  }

  function playCharacterClick(id) {
    var state = characterStates[id];
    if (!state || !state.visible || configMode) return;
    if (playActionThenIdle(state)) {
      send("characterClicked", { id: id });
    }
  }

  function playRandomDragAnimation(id) {
    var state = characterStates[id];
    if (!state || !state.visible || !state.spine || !state.spine.state) return;

    var candidates = state.animations.filter(function (name) {
      var normalized = String(name || "").toLowerCase();
      return normalized !== "idle" && normalized !== "action";
    });

    if (candidates.length === 0) {
      playIdle(state, true);
      return;
    }

    var animation = candidates[Math.floor(Math.random() * candidates.length)];
    playAnim(id, animation, true, true);
    send("characterDragAnimation", { id: id, animation: animation });
  }

  function endCharacterDrag(id) {
    var state = characterStates[id];
    if (state) {
      playIdle(state, true);
      scheduleHitMask(state);
    }
    characterDragActive = false;
    updateCanvasCursor();
    setHitRegionsPaused(false);
  }

  function playIdle(state, force) {
    if (!state || !state.spine || !state.spine.state) return;
    if (state.animations.indexOf("idle") < 0) return;
    if (!force && state.animation === "idle") return;

    try {
      state.spine.state.setAnimation(0, "idle", true);
      state.animation = "idle";
    } catch (e) {
      log("Idle error: " + e.message);
    }
  }

  function playActionThenIdle(state) {
    if (!state || !state.spine || !state.spine.state) return false;
    if (state.animations.indexOf("action") < 0) {
      playIdle(state, false);
      return false;
    }

    var now = Date.now();
    if (now - state.lastActionAt < ACTION_CLICK_COOLDOWN_MS) return false;
    state.lastActionAt = now;

    try {
      state.spine.state.setAnimation(0, "action", false);
      state.animation = "action";
      if (state.animations.indexOf("idle") >= 0) {
        state.spine.state.addAnimation(0, "idle", true, 0);
      }
      return true;
    } catch (e) {
      log("Action error: " + e.message);
      return false;
    }
  }

  function playAnim(id, name, loop, force) {
    var state = characterStates[id];
    if (!state || !state.spine || !state.spine.state || !name) return;
    if (!force && name === state.animation) return;
    try {
      state.spine.state.setAnimation(0, name, loop !== false);
      state.animation = name;
    } catch (e) {
      log("Play error: " + e.message);
    }
  }

  function setScale(id, s) {
    var state = characterStates[id];
    if (!state) return;
    state.scale = clamp(s, MIN_SCALE, state.maxScale || MAX_ALLOWED_SCALE);
    relayoutState(state);
    sendCharacterScaleChanged(state);
  }

  function setSpeed(id, s) {
    var state = characterStates[id];
    if (!state) return;
    state.speed = s;
    if (state.spine && state.spine.state) {
      state.spine.state.timeScale = s;
    }
  }

  app.ticker.add(function () {
    var dt = app.ticker.deltaMS / 1000;
    Object.keys(characterStates).forEach(function (id) {
      var state = characterStates[id];
      if (state.visible && state.spine && state.spine.update) state.spine.update(dt);
    });
  });

  window.addEventListener("resize", relayoutAll);

  log("PIXI v" + PIXI.VERSION + " ready, pixi-spine loaded=" + !!PIXI.spine);
  send("ready");
})();

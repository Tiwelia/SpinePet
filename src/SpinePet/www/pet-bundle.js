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
  var characterStates = {};
  var resourceCache = {};
  var activeSingleCharacterId = null;

  var ctxMenu = document.createElement("div");
  ctxMenu.id = "spinepet-ctxmenu";
  ctxMenu.style.cssText =
    "display:none;position:fixed;z-index:9999;background:#1E1E2E;border:1px solid #45475A;" +
    "border-radius:8px;padding:4px 0;min-width:160px;max-height:320px;overflow-y:auto;" +
    "box-shadow:0 8px 32px rgba(0,0,0,0.6);font-family:Segoe UI,sans-serif;font-size:13px;";
  document.body.appendChild(ctxMenu);

  function hideCtxMenu() { ctxMenu.style.display = "none"; }
  document.addEventListener("click", function (e) {
    if (!ctxMenu.contains(e.target)) hideCtxMenu();
  });

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", handleMsg);
  }

  function send(type, data) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(JSON.stringify({ type: type, data: data || {} }));
    }
  }

  function log(msg) {
    console.log("[SpinePet]", msg);
    send("log", { message: msg });
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
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
        speed: 0.5,
        animation: "",
        animations: [],
        x: 200,
        y: 200,
        visible: false,
        loadSeq: 0,
        timer: 0,
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
    log("layout id=" + state.id + " x=" + state.x + " y=" + state.y +
      " scale=" + state.scale + " visible=" + state.visible +
      " bounds=" + JSON.stringify(state.bounds));
  }

  function relayoutAll() {
    Object.keys(characterStates).forEach(function (id) {
      relayoutState(characterStates[id]);
    });
  }

  function destroySpine(state) {
    if (!state || !state.spine) return;
    app.stage.removeChild(state.spine);
    try { state.spine.destroy(); } catch (e) {}
    state.spine = null;
    state.bounds = null;
    state.animations = [];
    state.animation = "";
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
      if (state.animation && state.animations.indexOf(state.animation) >= 0) {
        state.spine.state.setAnimation(0, state.animation, true);
      } else if (state.animations.length > 0) {
        state.animation = state.animations[0];
        state.spine.state.setAnimation(0, state.animation, true);
      }
    }
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
        case "load":
          activeSingleCharacterId = "single";
          var singleData = m.data || {};
          var singleState = getState(activeSingleCharacterId);
          singleState.x = app.screen.width / 2;
          singleState.y = app.screen.height - 24;
          singleState.visible = true;
          singleState.scale = singleData.scale || singleState.scale;
          singleState.speed = 0.5;
          singleState.animation = "";
          loadCharacter(activeSingleCharacterId, singleData.skel || "", singleData.atlas || "", singleData.timeoutMs);
          break;
        case "showCharacter":
          var showData = m.data || {};
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
          app.render();
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
          var moveState = characterStates[(m.data || {}).id];
          if (moveState) {
            moveState.x = (m.data || {}).x || 0;
            moveState.y = (m.data || {}).y || 0;
            relayoutState(moveState);
          }
          break;
        case "play":
          playAnim(activeSingleCharacterId, m.data.name, m.data.loop);
          break;
        case "playCharacterAnimation":
          playAnim((m.data || {}).id, (m.data || {}).name, (m.data || {}).loop);
          break;
        case "setScale":
          setScale(activeSingleCharacterId, m.data.scale);
          break;
        case "setCharacterScale":
          setScale((m.data || {}).id, (m.data || {}).scale);
          break;
        case "setSpeed":
          setSpeed(activeSingleCharacterId, m.data.speed);
          break;
        case "setCharacterSpeed":
          setSpeed((m.data || {}).id, (m.data || {}).speed);
          break;
        case "setCharacterConfigMode":
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

  app.view.addEventListener("contextmenu", function (e) {
    e.preventDefault();
    var menuState = activeSingleCharacterId ? characterStates[activeSingleCharacterId] : null;
    var animNames = menuState ? menuState.animations : [];
    if (animNames.length === 0) return;

    ctxMenu.innerHTML = "";
    ctxMenu.style.display = "block";
    ctxMenu.style.left = e.clientX + "px";
    ctxMenu.style.top = Math.min(e.clientY, window.innerHeight - 340) + "px";

    var hdr = document.createElement("div");
    hdr.style.cssText = "padding:6px 14px;color:#6C7086;font-size:11px;font-weight:600;text-transform:uppercase;";
    hdr.textContent = "Animations";
    ctxMenu.appendChild(hdr);
    var sep = document.createElement("div");
    sep.style.cssText = "height:1px;background:#313244;margin:2px 0;";
    ctxMenu.appendChild(sep);

    animNames.forEach(function (name) {
      var item = document.createElement("div");
      item.style.cssText =
        "padding:6px 14px;color:#CDD6F4;cursor:pointer;" +
        (menuState && name === menuState.animation ? "background:#45475A;font-weight:600;" : "");
      item.textContent = name;
      item.addEventListener("mouseenter", function () {
        if (!menuState || name !== menuState.animation) item.style.background = "#313244";
      });
      item.addEventListener("mouseleave", function () {
        if (!menuState || name !== menuState.animation) item.style.background = "";
      });
      item.addEventListener("click", function () {
        playAnim(activeSingleCharacterId, name, true);
        hideCtxMenu();
      });
      ctxMenu.appendChild(item);
    });
  });

  function playAnim(id, name, loop) {
    var state = characterStates[id];
    if (!state || !state.spine || !state.spine.state || !name) return;
    if (name === state.animation) return;
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
    if (id === activeSingleCharacterId) {
      send("scaleChanged", { scale: state.scale, maxScale: state.maxScale || MAX_ALLOWED_SCALE });
    } else {
      sendCharacterScaleChanged(state);
    }
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
      if (state.spine && state.spine.update) state.spine.update(dt);
    });
  });

  window.addEventListener("resize", relayoutAll);

  log("PIXI v" + PIXI.VERSION + " ready, pixi-spine loaded=" + !!PIXI.spine);
  send("ready");
})();

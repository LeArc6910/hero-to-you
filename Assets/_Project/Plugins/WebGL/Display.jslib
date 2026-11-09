// Browser facts the game needs that Unity does not expose. Called from Client/WebBridge.cs in web builds only.
mergeInto(LibraryManager.library, {
  // CSS pixels per device pixel; Unity draws the canvas at the device's full resolution.
  GetDevicePixelRatio: function () {
    return window.devicePixelRatio || 1;
  },

  // 1 when the last pointer on the page was a finger (the page's script notes it, Assets/WebGLTemplates/Fit), or the
  // device has a touch screen and no pointer has been seen yet.
  IsTouchInput: function () {
    if (window.heroLastPointer) return window.heroLastPointer === "touch" ? 1 : 0;
    return (navigator.maxTouchPoints || 0) > 0 && !window.matchMedia("(pointer: fine)").matches ? 1 : 0;
  },
});

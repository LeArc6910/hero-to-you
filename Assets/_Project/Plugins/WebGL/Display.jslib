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

  // The bug report onto the clipboard: the modern API when allowed, else the old copy command. 1 when it took.
  CopyText: function (text) {
    var value = UTF8ToString(text);
    try {
      if (navigator.clipboard && window.isSecureContext) { navigator.clipboard.writeText(value); return 1; }
      var area = document.createElement("textarea");
      area.value = value;
      document.body.appendChild(area);
      area.select();
      var ok = document.execCommand("copy");
      document.body.removeChild(area);
      return ok ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  // The bug report as a downloaded text file.
  DownloadText: function (name, text) {
    var blob = new Blob([UTF8ToString(text)], { type: "text/plain" });
    var link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = UTF8ToString(name);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  },

  // A short buzz on phones that can (the settings' 진동); nothing elsewhere.
  Vibrate: function (ms) {
    if (navigator.vibrate) navigator.vibrate(ms);
  },
});

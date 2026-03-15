window.trackingHelper = {
    /**
     * Returns { x, y } as ratios in [0.0, 1.0] relative to the image element.
     * Called from Blazor via JSInterop when the user clicks the stream image.
     */
    getClickRatio: function (imgElementId, clientX, clientY) {
        const img = document.getElementById(imgElementId);
        if (!img) return { x: 0, y: 0 };
        const rect = img.getBoundingClientRect();
        return {
            x: Math.max(0, Math.min(1, (clientX - rect.left) / rect.width)),
            y: Math.max(0, Math.min(1, (clientY - rect.top) / rect.height))
        };
    }
};

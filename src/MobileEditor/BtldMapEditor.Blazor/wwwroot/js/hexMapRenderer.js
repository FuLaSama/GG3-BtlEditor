window.HexMapRenderer = {
    canvas: null,
    ctx: null,
    dotNetRef: null,
    mapWidth: 0,
    mapHeight: 0,
    cellsData: [],
    
    // Viewport transform
    zoom: 1.0,
    panX: 50,
    panY: 50,
    
    // Selection & Paint mode
    selectedCellIdx: -1,
    brushMode: false,
    brushRadius: 0,
    isDragging: false,
    dragStartX: 0,
    dragStartY: 0,
    initialPanX: 0,
    initialPanY: 0,
    
    // Hexagon dimensions
    hexRadius: 32,
    hexW: 64,
    hexH: 55.4256,
    xSpacing: 48,
    ySpacing: 55.4256,

    init: function (canvasId, dotNetRef) {
        this.canvas = document.getElementById(canvasId);
        if (!this.canvas) return;
        this.ctx = this.canvas.getContext('2d');
        this.dotNetRef = dotNetRef;

        this.resizeCanvas();
        window.addEventListener('resize', () => this.resizeCanvas());

        this.attachEvents();
        this.requestRender();
    },

    resizeCanvas: function () {
        if (!this.canvas) return;
        const rect = this.canvas.parentElement.getBoundingClientRect();
        this.canvas.width = rect.width * window.devicePixelRatio;
        this.canvas.height = rect.height * window.devicePixelRatio;
        this.requestRender();
    },

    loadMapData: function (width, height, cellsJson) {
        this.mapWidth = width;
        this.mapHeight = height;
        try {
            this.cellsData = typeof cellsJson === 'string' ? JSON.parse(cellsJson) : cellsJson;
        } catch (e) {
            console.error("Failed to parse cells data:", e);
            this.cellsData = [];
        }
        this.requestRender();
    },

    setSelectedCell: function (idx) {
        this.selectedCellIdx = idx;
        this.requestRender();
    },

    setBrushMode: function (enabled, radius) {
        this.brushMode = enabled;
        this.brushRadius = radius;
        this.requestRender();
    },

    setZoom: function (zoomVal) {
        this.zoom = Math.max(0.2, Math.min(4.0, zoomVal));
        this.requestRender();
    },

    resetView: function () {
        this.zoom = 1.0;
        this.panX = 50;
        this.panY = 50;
        this.requestRender();
    },

    attachEvents: function () {
        const c = this.canvas;
        if (!c) return;

        // Pointer / Mouse / Touch Pan & Zoom & Paint
        c.addEventListener('pointerdown', (e) => {
            this.isDragging = true;
            this.dragStartX = e.clientX;
            this.dragStartY = e.clientY;
            this.initialPanX = this.panX;
            this.initialPanY = this.panY;
            c.setPointerCapture(e.pointerId);

            const cellIdx = this.getTileIdxAtPointer(e.clientX, e.clientY);
            if (cellIdx >= 0) {
                if (this.brushMode) {
                    this.dotNetRef.invokeMethodAsync('OnPaintTileRequested', cellIdx);
                } else {
                    this.dotNetRef.invokeMethodAsync('OnTileSelected', cellIdx);
                }
            }
        });

        c.addEventListener('pointermove', (e) => {
            if (!this.isDragging) return;

            if (this.brushMode) {
                // Freeze pan during brush paint
                const cellIdx = this.getTileIdxAtPointer(e.clientX, e.clientY);
                if (cellIdx >= 0) {
                    this.dotNetRef.invokeMethodAsync('OnPaintTileRequested', cellIdx);
                }
            } else {
                // Pan map
                const dx = (e.clientX - this.dragStartX);
                const dy = (e.clientY - this.dragStartY);
                this.panX = this.initialPanX + dx;
                this.panY = this.initialPanY + dy;
                this.requestRender();
            }
        });

        c.addEventListener('pointerup', (e) => {
            this.isDragging = false;
            try { c.releasePointerCapture(e.pointerId); } catch {}
        });

        c.addEventListener('wheel', (e) => {
            e.preventDefault();
            const zoomFactor = e.deltaY < 0 ? 1.1 : 0.9;
            this.zoom = Math.max(0.2, Math.min(4.0, this.zoom * zoomFactor));
            this.requestRender();
        }, { passive: false });
    },

    getTileIdxAtPointer: function (clientX, clientY) {
        if (this.mapWidth <= 0 || this.mapHeight <= 0) return -1;
        const rect = this.canvas.getBoundingClientRect();
        const screenX = (clientX - rect.left) * window.devicePixelRatio;
        const screenY = (clientY - rect.top) * window.devicePixelRatio;

        // Invert view transform
        const worldX = (screenX - this.panX * window.devicePixelRatio) / (this.zoom * window.devicePixelRatio);
        const worldY = (screenY - this.panY * window.devicePixelRatio) / (this.zoom * window.devicePixelRatio);

        let closestIdx = -1;
        let minDistSq = Infinity;

        for (let y = 0; y < this.mapHeight; y++) {
            for (let x = 0; x < this.mapWidth; x++) {
                const cx = x * this.xSpacing + this.hexRadius;
                const cy = y * this.ySpacing + (x % 2 !== 0 ? this.ySpacing / 2 : 0) + this.hexH / 2;
                const dx = worldX - cx;
                const dy = worldY - cy;
                const distSq = dx * dx + dy * dy;
                if (distSq < minDistSq) {
                    minDistSq = distSq;
                    closestIdx = y * this.mapWidth + x;
                }
            }
        }

        if (minDistSq <= (this.hexRadius * 1.2) * (this.hexRadius * 1.2)) {
            return closestIdx;
        }
        return -1;
    },

    requestRender: function () {
        if (!this.ctx || !this.canvas) return;
        requestAnimationFrame(() => this.render());
    },

    render: function () {
        const ctx = this.ctx;
        const canvas = this.canvas;
        const dpr = window.devicePixelRatio || 1;

        ctx.clearRect(0, 0, canvas.width, canvas.height);

        // Background
        ctx.fillStyle = "#0f172a"; // Deep navy dark mode
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        if (this.mapWidth <= 0 || this.mapHeight <= 0) return;

        ctx.save();
        ctx.translate(this.panX * dpr, this.panY * dpr);
        ctx.scale(this.zoom * dpr, this.zoom * dpr);

        const R = this.hexRadius;
        const H = this.hexH;

        // Render hex cells
        for (let y = 0; y < this.mapHeight; y++) {
            for (let x = 0; x < this.mapWidth; x++) {
                const idx = y * this.mapWidth + x;
                const cell = this.cellsData[idx] || {};

                const cx = x * this.xSpacing + R;
                const cy = y * this.ySpacing + (x % 2 !== 0 ? this.ySpacing / 2 : 0) + H / 2;

                // Base terrain color lookup
                let baseColor = "#334155";
                const bt = cell.bt || 0;
                if (bt === 0) baseColor = "#38bdf8"; // Water/Sea
                else if (bt === 1) baseColor = "#4ade80"; // Plains
                else if (bt === 2) baseColor = "#facc15"; // Desert
                else if (bt === 3) baseColor = "#94a3b8"; // Mountain
                else if (bt === 4) baseColor = "#15803d"; // Forest
                else baseColor = "#64748b";

                // Draw Hexagon Polygon
                this.drawHexagon(ctx, cx, cy, R - 1, baseColor, "#1e293b", 1);

                // Doodad / Variation Indicator
                if (cell.v > 0) {
                    ctx.fillStyle = "rgba(255, 255, 255, 0.25)";
                    ctx.beginPath();
                    ctx.arc(cx, cy, R * 0.4, 0, Math.PI * 2);
                    ctx.fill();
                }

                // Building / Fort Indicator
                if (cell.bldg != null) {
                    ctx.fillStyle = "#f97316"; // Orange building icon
                    ctx.beginPath();
                    ctx.rect(cx - 10, cy - 10, 20, 20);
                    ctx.fill();
                    ctx.strokeStyle = "#ffffff";
                    ctx.lineWidth = 1.5;
                    ctx.stroke();

                    ctx.fillStyle = "#ffffff";
                    ctx.font = "bold 10px sans-serif";
                    ctx.textAlign = "center";
                    ctx.textBaseline = "middle";
                    ctx.fillText("建" + cell.bldg, cx, cy);
                }

                if (cell.fort != null) {
                    ctx.fillStyle = "#a855f7"; // Purple fort icon
                    ctx.beginPath();
                    ctx.arc(cx, cy, 12, 0, Math.PI * 2);
                    ctx.fill();
                    ctx.strokeStyle = "#ffffff";
                    ctx.lineWidth = 1.5;
                    ctx.stroke();

                    ctx.fillStyle = "#ffffff";
                    ctx.font = "bold 10px sans-serif";
                    ctx.textAlign = "center";
                    ctx.textBaseline = "middle";
                    ctx.fillText("堡", cx, cy);
                }

                // Unit Indicator
                if (cell.unit != null) {
                    const u = cell.unit;
                    ctx.fillStyle = "#ef4444"; // Red unit badge
                    ctx.beginPath();
                    ctx.arc(cx, cy, 14, 0, Math.PI * 2);
                    ctx.fill();
                    ctx.strokeStyle = "#ffffff";
                    ctx.lineWidth = 2;
                    ctx.stroke();

                    ctx.fillStyle = "#ffffff";
                    ctx.font = "bold 11px sans-serif";
                    ctx.textAlign = "center";
                    ctx.textBaseline = "middle";
                    ctx.fillText("军" + u.id, cx, cy);
                }

                // Selected Cell Ring Highlight
                if (idx === this.selectedCellIdx) {
                    this.drawHexagon(ctx, cx, cy, R + 2, "transparent", "#fbbf24", 4);
                }
            }
        }

        ctx.restore();
    },

    drawHexagon: function (ctx, cx, cy, r, fillColor, strokeColor, lineWidth) {
        ctx.beginPath();
        for (let i = 0; i < 6; i++) {
            const angle = (Math.PI / 3) * i;
            const x = cx + r * Math.cos(angle);
            const y = cy + r * Math.sin(angle);
            if (i === 0) ctx.moveTo(x, y);
            else ctx.lineTo(x, y);
        }
        ctx.closePath();

        if (fillColor && fillColor !== "transparent") {
            ctx.fillStyle = fillColor;
            ctx.fill();
        }
        if (strokeColor && lineWidth > 0) {
            ctx.strokeStyle = strokeColor;
            ctx.lineWidth = lineWidth;
            ctx.stroke();
        }
    }
};

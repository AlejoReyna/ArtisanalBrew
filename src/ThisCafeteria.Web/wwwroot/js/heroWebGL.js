/**
 * Artisanal Brew — Hero grain overlay
 *
 * Flickering TV-static noise rendered on a transparent WebGL canvas, blended
 * over the hero background video via CSS mix-blend-mode. Regenerates at a
 * low, chunky frame rate (not per-render-frame) so it reads as coarse static
 * rather than smooth per-pixel dithering.
 */

(function (global) {
    'use strict';
    const MODULE_NAME = 'heroWebGL';

    const VERT = `
        attribute vec2 a_pos;
        void main() {
            gl_Position = vec4(a_pos, 0.0, 1.0);
        }
    `;

    const FRAG = `
        precision highp float;

        uniform vec2 u_res;
        uniform float u_time;
        uniform float u_animate;
        uniform float u_grainSize;
        uniform float u_intensity;
        uniform float u_fps;

        float hash(vec2 p) {
            return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
        }

        void main() {
            // Snap to a coarse cell grid and a stepped clock so the static
            // reads as flickering grain, not a smooth per-pixel shimmer.
            vec2 cell = floor(gl_FragCoord.xy / u_grainSize);
            float frame = floor(u_time * u_animate * u_fps);
            float n = hash(cell + frame * 17.0);
            gl_FragColor = vec4(vec3(n), n * u_intensity);
        }
    `;

    function compileShader(gl, type, src) {
        const s = gl.createShader(type);
        gl.shaderSource(s, src);
        gl.compileShader(s);
        if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) {
            console.error('[heroWebGL] Shader error:', gl.getShaderInfoLog(s));
            gl.deleteShader(s);
            return null;
        }
        return s;
    }

    function createProgram(gl, vsSrc, fsSrc) {
        const vs = compileShader(gl, gl.VERTEX_SHADER, vsSrc);
        const fs = compileShader(gl, gl.FRAGMENT_SHADER, fsSrc);
        if (!vs || !fs) return null;
        const p = gl.createProgram();
        gl.attachShader(p, vs);
        gl.attachShader(p, fs);
        gl.linkProgram(p);
        if (!gl.getProgramParameter(p, gl.LINK_STATUS)) {
            console.error('[heroWebGL] Link error:', gl.getProgramInfoLog(p));
            gl.deleteProgram(p);
            return null;
        }
        return p;
    }

    class GrainOverlay {
        constructor(canvas) {
            this.canvas = canvas;
            this.gl = canvas.getContext('webgl', { alpha: true, antialias: false, preserveDrawingBuffer: false });
            if (!this.gl) throw new Error('WebGL not supported');
            const gl = this.gl;

            this.program = createProgram(gl, VERT, FRAG);
            if (!this.program) throw new Error('Shader compile failed');

            this.uRes = gl.getUniformLocation(this.program, 'u_res');
            this.uTime = gl.getUniformLocation(this.program, 'u_time');
            this.uAnimate = gl.getUniformLocation(this.program, 'u_animate');
            this.uGrainSize = gl.getUniformLocation(this.program, 'u_grainSize');
            this.uIntensity = gl.getUniformLocation(this.program, 'u_intensity');
            this.uFps = gl.getUniformLocation(this.program, 'u_fps');
            this.aPos = gl.getAttribLocation(this.program, 'a_pos');

            this.quadBuf = gl.createBuffer();
            gl.bindBuffer(gl.ARRAY_BUFFER, this.quadBuf);
            gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]), gl.STATIC_DRAW);

            gl.enable(gl.BLEND);
            gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

            this.reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            this.grainSize = 1.6;
            this.intensity = 0.08;
            this.fps = 14;
            this.width = 0;
            this.height = 0;
            this.dpr = Math.min(window.devicePixelRatio || 1, 2);
            this.startTime = performance.now();
            this.running = false;
            this.rafId = null;
            this.visible = true;

            this.parent = canvas.parentElement || canvas;
            this._onR = this.onResize.bind(this);
            this._onV = this.onVisibilityChange.bind(this);

            window.addEventListener('resize', this._onR, { passive: true });
            document.addEventListener('visibilitychange', this._onV);

            this.observer = new IntersectionObserver((entries) => {
                this.visible = entries[0].isIntersecting;
                if (this.visible && !this.running) this.start();
            }, { threshold: 0 });
            this.observer.observe(canvas);

            this.resizeObserver = new ResizeObserver(() => this.onResize());
            this.resizeObserver.observe(this.parent);
            this.onResize();
        }

        onResize() {
            const r = this.canvas.getBoundingClientRect();
            const w = Math.max(1, Math.round(r.width * this.dpr));
            const h = Math.max(1, Math.round(r.height * this.dpr));
            if (this.width === w && this.height === h) return;
            this.width = w;
            this.height = h;
            this.canvas.width = w;
            this.canvas.height = h;
            this.gl.viewport(0, 0, w, h);
            if (!this.running) this.renderFrame();
        }

        onVisibilityChange() {
            if (document.hidden) {
                this.visible = false;
            } else {
                this.visible = true;
                if (!this.running) this.start();
            }
        }

        start() {
            if (this.running) return;
            if (this.reduced) {
                this.gl.clear(this.gl.COLOR_BUFFER_BIT);
                return;
            }
            this.running = true;
            this.loop();
        }

        stop() {
            this.running = false;
            if (this.rafId) {
                cancelAnimationFrame(this.rafId);
                this.rafId = null;
            }
        }

        loop() {
            if (!this.running) return;
            this.rafId = requestAnimationFrame(() => this.loop());
            if (!this.visible || document.hidden) return;
            this.renderFrame();
        }

        renderFrame() {
            const gl = this.gl;
            const t = (performance.now() - this.startTime) * 0.001;
            gl.clear(gl.COLOR_BUFFER_BIT);
            gl.useProgram(this.program);
            gl.bindBuffer(gl.ARRAY_BUFFER, this.quadBuf);
            gl.enableVertexAttribArray(this.aPos);
            gl.vertexAttribPointer(this.aPos, 2, gl.FLOAT, false, 0, 0);
            gl.uniform2f(this.uRes, this.width, this.height);
            gl.uniform1f(this.uTime, t);
            gl.uniform1f(this.uAnimate, this.reduced ? 0.0 : 1.0);
            gl.uniform1f(this.uGrainSize, this.grainSize * this.dpr);
            gl.uniform1f(this.uIntensity, this.intensity);
            gl.uniform1f(this.uFps, this.fps);
            gl.drawArrays(gl.TRIANGLES, 0, 6);
        }

        destroy() {
            this.stop();
            window.removeEventListener('resize', this._onR);
            document.removeEventListener('visibilitychange', this._onV);
            if (this.observer) {
                this.observer.disconnect();
                this.observer = null;
            }
            if (this.resizeObserver) {
                this.resizeObserver.disconnect();
                this.resizeObserver = null;
            }
            const gl = this.gl;
            gl.deleteBuffer(this.quadBuf);
            gl.deleteProgram(this.program);
            const ext = gl.getExtension('WEBGL_lose_context');
            if (ext) ext.loseContext();
        }
    }

    const instances = new Map();
    global[MODULE_NAME] = {
        init(id) {
            const c = typeof id === 'string' ? document.getElementById(id) : id;
            if (!c) { console.warn('[heroWebGL] Canvas not found:', id); return; }
            const key = c.id || c;
            if (instances.has(key)) instances.get(key).destroy();
            try {
                const scene = new GrainOverlay(c);
                scene.start();
                instances.set(key, scene);
            } catch (e) {
                console.warn('[heroWebGL] Init failed:', e);
            }
        },
        destroy(id) {
            const key = typeof id === 'string' ? id : id;
            const scene = instances.get(key);
            if (scene) {
                scene.destroy();
                instances.delete(key);
            }
        },
        destroyAll() {
            instances.forEach((s) => s.destroy());
            instances.clear();
        }
    };
})(window);

// 試作品の 3D の共通部品：GLB の読み込み、研究所と同じ光、発明台の橙の輪、カメラの合わせ方。
// 1件のページ（回せる試作品）と一覧（ゆっくり回る小さな試作品）で使う。
// 本物らしく見せる決まり：撮影台の床と面の光、柔らかい影、部品どうしのすき間の陰り（AO）、
// 樹脂の上塗りのつや、表面の細かいざらつき（樹脂）とヘアライン（金属）。
import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { RoomEnvironment } from "three/addons/environments/RoomEnvironment.js";

export { THREE };

/** 描画装置。色の扱いと明るさは 1 件のページと一覧で同じにする。影は柔らかい縁で落とす。 */
export function createRenderer(options = {}) {
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, ...options });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.VSMShadowMap;
  return renderer;
}

const envs = new WeakMap();
/** 研究所の部屋の映り込みと、上からの光。 */
export function createScene(renderer) {
  if (!envs.has(renderer)) envs.set(renderer, new THREE.PMREMGenerator(renderer).fromScene(new RoomEnvironment(), 0.04).texture);
  const scene = new THREE.Scene();
  scene.environment = envs.get(renderer);
  scene.environmentIntensity = 0.55;
  const key = new THREE.DirectionalLight(0xffffff, 1.6); key.position.set(2, 4, 3); scene.add(key);
  scene.add(new THREE.HemisphereLight(0xdff8ff, 0x0a0f12, 0.5));
  return scene;
}

// ---------------- 表面 ----------------
// 細かい凹凸は貼り絵を使わず、部品の中の位置から作る（GLB に UV が無いため）。
// 部品が回っても模様は部品に付いたまま動く。1 画素より細かくなる遠さでは消す（ちらつかないように）。
const GRAIN_VERTEX = /* glsl */ `
uniform vec3 uGrainFreq;
varying vec3 vGrain;
`;
const GRAIN_FRAGMENT = /* glsl */ `
uniform float uGrainAmp;
varying vec3 vGrain;
float ilabHash(vec3 p) {
  p = fract(p * 0.3183099 + 0.1);
  p *= 17.0;
  return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}
float ilabNoise(vec3 x) {
  vec3 i = floor(x), f = fract(x);
  f = f * f * (3.0 - 2.0 * f);
  return mix(mix(mix(ilabHash(i), ilabHash(i + vec3(1, 0, 0)), f.x), mix(ilabHash(i + vec3(0, 1, 0)), ilabHash(i + vec3(1, 1, 0)), f.x), f.y),
             mix(mix(ilabHash(i + vec3(0, 0, 1)), ilabHash(i + vec3(1, 0, 1)), f.x), mix(ilabHash(i + vec3(0, 1, 1)), ilabHash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
vec3 ilabBump(vec3 surfPos, vec3 n, vec2 dHdxy, float face) {
  vec3 sx = normalize(dFdx(surfPos)), sy = normalize(dFdy(surfPos));
  vec3 r1 = cross(sy, n), r2 = cross(n, sx);
  float det = dot(sx, r1) * face;
  vec3 grad = sign(det) * (dHdxy.x * r1 + dHdxy.y * r2);
  return normalize(abs(det) * n - grad);
}
`;
const GRAIN_APPLY = /* glsl */ `
{
  float cells = length(fwidth(vGrain));
  float amp = uGrainAmp * clamp(1.4 - cells, 0.0, 1.0);
  if (amp > 0.0) {
    float h = ilabNoise(vGrain) + 0.5 * ilabNoise(vGrain * 2.07 + 7.1);
    normal = ilabBump(-vViewPosition, normal, vec2(dFdx(h), dFdy(h)) * amp, faceDirection);
  }
}
`;

/**
 * 表面の種類ごとの模様。cell は模様の1粒の大きさ（m）、tilt は面の傾きの強さ。
 * 金属は長い向きに流れる細い筋（ヘアライン）、樹脂は細かい梨地、ゴム・布は粗い梨地。
 */
const SURFACES = {
  metal: { cell: 0.0012, stretch: 40, tilt: 0.05 },
  plastic: { cell: 0.0016, stretch: 1, tilt: 0.035 },
  matte: { cell: 0.0024, stretch: 1, tilt: 0.06 },
};

function surfaceOf(m) {
  if (m.metalness >= 0.5) return "metal";
  return m.roughness > 0.7 ? "matte" : "plastic";
}

/** 書き出したままの素材を、上塗り・ざらつき付きの素材にする。半透明（覆いなど）と光る部品はそのまま。 */
function finish(src, mesh) {
  if (!src.isMeshStandardMaterial || src.transparent) return src;
  const kind = surfaceOf(src);
  const m = new THREE.MeshPhysicalMaterial({
    name: src.name, color: src.color, map: src.map, side: src.side,
    emissive: src.emissive, emissiveIntensity: src.emissiveIntensity, emissiveMap: src.emissiveMap,
    metalness: src.metalness,
    roughness: kind === "metal" ? Math.max(src.roughness, 0.3) : kind === "plastic" ? Math.max(src.roughness, 0.42) : src.roughness,
    // 樹脂は薄い上塗りのつや（下の色は少しだけ鈍く、表面にくっきり映り込む）
    clearcoat: kind === "plastic" ? 0.7 : 0,
    clearcoatRoughness: 0.16,
    // 金属は周りの映り込みを強く（明るい所と暗い所の差で金属らしく見える）
    envMapIntensity: kind === "metal" ? 1.6 : 1,
  });
  const s = SURFACES[kind];
  // 部品の拡大を掛けた実寸で模様を作る。筋は部品のいちばん長い向きに流す
  const scale = mesh.getWorldScale(new THREE.Vector3());
  const size = new THREE.Box3().setFromBufferAttribute(mesh.geometry.attributes.position).getSize(new THREE.Vector3()).multiply(scale);
  const long = size.x >= size.y && size.x >= size.z ? "x" : size.y >= size.z ? "y" : "z";
  const freq = scale.clone().divideScalar(s.cell);
  freq[long] /= s.stretch;
  m.onBeforeCompile = (shader) => {
    shader.uniforms.uGrainFreq = { value: freq };
    shader.uniforms.uGrainAmp = { value: s.tilt * s.cell };
    shader.vertexShader = GRAIN_VERTEX + shader.vertexShader.replace("#include <begin_vertex>", "#include <begin_vertex>\n  vGrain = transformed * uGrainFreq;");
    shader.fragmentShader = GRAIN_FRAGMENT + shader.fragmentShader.replace("#include <normal_fragment_maps>", "#include <normal_fragment_maps>\n" + GRAIN_APPLY);
  };
  m.customProgramCacheKey = () => "ilab-grain";
  return m;
}

/**
 * 試作品の GLB を読む。共有ページでは GLB を配れないので、文字にした GLB（window.ILAB_GLBS[key]）があればそれを使う。
 * 半透明の部品（覆いなど）は、中の部品が見えるように透かす。
 */
export function loadModel(url, key) {
  return new Promise((resolve, reject) => {
    const done = (gltf) => {
      gltf.scene.updateMatrixWorld(true);
      gltf.scene.traverse((o) => {
        if (!o.isMesh) return;
        o.material = o.material.clone();
        if (o.material.transparent) { o.material.opacity = Math.min(o.material.opacity, 0.45); o.material.depthWrite = false; }
        else o.material = finish(o.material, o);
      });
      resolve(gltf.scene);
    };
    const b64 = key && window.ILAB_GLBS ? window.ILAB_GLBS[key] : null;
    if (b64) {
      const bin = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
      new GLTFLoader().parse(bin.buffer, "", done, reject);
    } else new GLTFLoader().load(url, done, undefined, reject);
  });
}

/** 発明台の輪の半径（試作品の横の半分の幅から）。小さい試作品でも輪は 18 cm より小さくしない。 */
export const ringRadius = (halfWidth) => Math.max(0.18, halfWidth * 1.18);

/**
 * 大きさの情報：中心・外接球の半径・床の高さ・横の半分の幅・寸法。
 * fit はカメラを合わせる半径：試作品と足元の輪（目盛りまで）の両方が枠に収まる大きさ。
 */
export function measure(root) {
  const box = new THREE.Box3().setFromObject(root);
  const size = box.getSize(new THREE.Vector3());
  const center = box.getCenter(new THREE.Vector3());
  const halfWidth = Math.max(size.x, size.z) / 2;
  const radius = Math.max(0.12, size.length() / 2);
  const ring = ringRadius(halfWidth) * 1.13;
  const fit = Math.max(radius, Math.hypot(ring, size.y / 2));
  return { center, size, floor: box.min.y, halfWidth, radius, fit };
}

/** 発明台の輪：試作品の足元に、研究所の床と同じ橙の輪と細かい目盛り、前（+Z）の三角。 */
export function stageRing(c, y, r) {
  const g = new THREE.Group();
  const R = ringRadius(r);
  const ring = new THREE.Mesh(new THREE.RingGeometry(R, R * 1.012 + 0.004, 160), new THREE.MeshBasicMaterial({ color: 0xff6a26, side: THREE.DoubleSide }));
  ring.rotation.x = -Math.PI / 2; ring.position.set(c.x, y - 0.002, c.z); g.add(ring);
  const pts = [];
  for (let i = 0; i < 120; i++) {
    const a = (i / 120) * Math.PI * 2, long = i % 10 === 0;
    const r0 = R * 1.05, r1 = R * (long ? 1.13 : 1.08);
    pts.push(c.x + Math.sin(a) * r0, y, c.z + Math.cos(a) * r0, c.x + Math.sin(a) * r1, y, c.z + Math.cos(a) * r1);
  }
  const lines = new THREE.BufferGeometry(); lines.setAttribute("position", new THREE.Float32BufferAttribute(pts, 3));
  g.add(new THREE.LineSegments(lines, new THREE.LineBasicMaterial({ color: 0x116168 })));
  const tri = new THREE.Mesh(new THREE.CircleGeometry(R * 0.06, 3), new THREE.MeshBasicMaterial({ color: 0x2fe3e6, side: THREE.DoubleSide }));
  tri.rotation.set(-Math.PI / 2, 0, Math.PI / 2); tri.position.set(c.x, y, c.z + R * 1.2); g.add(tri);
  return g;
}

// ---------------- 撮影台と光 ----------------
let floorTex = null;
/**
 * 床の模様：奥ほど明るく（撮影台の奥の壁に光が回った感じ）、手前は暗い灰色。
 * 透明度は中ほどまで不透明で、外へ向かって背景に溶ける。
 */
function floorTextures() {
  if (floorTex) return floorTex;
  const make = (paint) => {
    const c = document.createElement("canvas");
    c.width = c.height = 256;
    paint(c.getContext("2d"));
    return new THREE.CanvasTexture(c);
  };
  const map = make((g) => {
    const grad = g.createLinearGradient(0, 0, 0, 256); // 上＝奥
    grad.addColorStop(0, "#80878c");
    grad.addColorStop(0.32, "#a3aaaf");
    grad.addColorStop(0.5, "#5d6368");
    grad.addColorStop(0.7, "#3d4246");
    grad.addColorStop(1, "#2a2e31");
    g.fillStyle = grad;
    g.fillRect(0, 0, 256, 256);
  });
  map.colorSpace = THREE.SRGBColorSpace;
  const alpha = make((g) => {
    const grad = g.createRadialGradient(128, 128, 0, 128, 128, 128);
    grad.addColorStop(0, "#fff");
    grad.addColorStop(0.55, "#fff");
    grad.addColorStop(1, "#000");
    g.fillStyle = grad;
    g.fillRect(0, 0, 256, 256);
  });
  return (floorTex = { map, alpha });
}

let areaReady = null;
/** 面の光を使う準備（初めの1回だけ）。 */
function areaLights() {
  return (areaReady ??= import("three/addons/lights/RectAreaLightUniformsLib.js").then((m) => m.RectAreaLightUniformsLib.init()));
}

// 光の位置（試作品の半径を 1 とする。見ている人から見て x＝右、z＝手前）。
// 主な光は左前の上の大きな面、縁の光は奥の大きな面（床に映って奥ほど明るくなる）
const KEY = new THREE.Vector3(-1.6, 2.2, 2.0), RIM = new THREE.Vector3(0.6, 1.6, -2.6);
const SUN = new THREE.Vector3(-1.1, 2.6, 1.5);

/**
 * 試作品を撮影台に置く：奥ほど明るい灰色の床、部品どうしと床に落ちる柔らかい影、大きな面の光。
 * floating（頭の上に付けるもの）は床を置かない（部品どうしの影は落とす）。
 * 返す light(yaw) を描く前に呼ぶと、見る向きに合わせて光を回す（いつも見ている人の左前の上から照らす）。
 */
export async function dress(scene, root, m, { floating = false } = {}) {
  await areaLights();
  const c = m.center, r = m.radius;
  root.traverse((o) => { if (o.isMesh) { o.castShadow = !o.material.transparent; o.receiveShadow = true; } });

  // 影を落とす光（向きは主な光と同じ）。面の光は影を落とさないので、こちらで落とす
  const sun = scene.children.find((o) => o.isDirectionalLight);
  sun.intensity = 0.9;
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.bias = -0.0003;
  sun.shadow.normalBias = r * 0.004;
  sun.shadow.radius = 10;
  sun.shadow.blurSamples = 16;
  Object.assign(sun.shadow.camera, { left: -r * 1.8, right: r * 1.8, top: r * 1.8, bottom: -r * 1.8, near: r * 0.05, far: r * 10 });
  sun.shadow.camera.updateProjectionMatrix();
  sun.target.position.copy(c);
  scene.add(sun.target);
  const hemi = scene.children.find((o) => o.isHemisphereLight);
  if (hemi) hemi.intensity = 0.35;

  const area = (color, intensity, size) => {
    const a = new THREE.RectAreaLight(color, intensity, size * r, size * r);
    scene.add(a);
    return a;
  };
  const key = area(0xffffff, 2.2, 1.6), rim = area(0xe4f4ff, 6, 1.2);

  let turn = null;
  if (!floating) {
    // 床：奥ほど明るい灰色の板（見る向きに合わせて回し、いつも奥が明るい）。面の光は受けず、影とすき間の陰り（AO）を受ける
    const { map, alpha } = floorTextures();
    const floor = new THREE.Mesh(new THREE.PlaneGeometry(r * 12, r * 12),
      new THREE.MeshLambertMaterial({ map, alphaMap: alpha, transparent: true }));
    floor.rotation.x = -Math.PI / 2;
    floor.receiveShadow = true;
    floor.renderOrder = -2;
    floor.userData.aoFloor = true;
    turn = new THREE.Group();
    turn.position.set(c.x, m.floor - 0.004, c.z);
    turn.add(floor);
    scene.add(turn);
  }

  const spin = (v, yaw) => new THREE.Vector3(v.x * Math.cos(yaw) + v.z * Math.sin(yaw), v.y, -v.x * Math.sin(yaw) + v.z * Math.cos(yaw));
  const place = (light, v, yaw, d) => { light.position.copy(c).addScaledVector(spin(v, yaw), r * d); light.lookAt?.(c); };
  return {
    light(yaw) {
      if (turn) turn.rotation.y = yaw;
      place(sun, SUN, yaw, 2.2);
      place(key, KEY, yaw, 1);
      place(rim, RIM, yaw, 1);
    },
  };
}

/**
 * 部品どうしのすき間・付け根の陰り（画面の奥行きから求める AO）。半透明の部品は計算に入れない（床は入れる）。
 * 返すもの：render()・setSize(w, h)（CSS の大きさ）・setRadius(試作品の半径)。
 */
export async function createAO(renderer, scene, cam) {
  const [{ EffectComposer }, { RenderPass }, { GTAOPass }, { OutputPass }] = await Promise.all([
    import("three/addons/postprocessing/EffectComposer.js"),
    import("three/addons/postprocessing/RenderPass.js"),
    import("three/addons/postprocessing/GTAOPass.js"),
    import("three/addons/postprocessing/OutputPass.js"),
  ]);
  const px = renderer.getDrawingBufferSize(new THREE.Vector2());
  const target = new THREE.WebGLRenderTarget(px.x, px.y, { type: THREE.HalfFloatType, samples: 4 });
  const composer = new EffectComposer(renderer, target);
  composer.addPass(new RenderPass(scene, cam));
  const ao = new GTAOPass(scene, cam, px.x, px.y);
  const hide = ao.overrideVisibility.bind(ao);
  ao.overrideVisibility = () => {
    hide();
    scene.traverse((o) => { if (o.isMesh && o.material.transparent && !o.userData.aoFloor) o.visible = false; });
  };
  ao.blendIntensity = 1;
  ao.updatePdMaterial({ lumaPhi: 10, depthPhi: 2, normalPhi: 3, radius: 6, rings: 2, samples: 16 });
  composer.addPass(ao);
  composer.addPass(new OutputPass());
  return {
    render() { composer.render(); },
    setSize(w, h) { composer.setPixelRatio(renderer.getPixelRatio()); composer.setSize(w, h); },
    setRadius(r) { ao.updateGtaoMaterial({ radius: r * 0.3, thickness: r * 0.5, distanceExponent: 1.2, distanceFallOff: 1, scale: 1.6, samples: 24 }); },
  };
}

/** 試作品の中心を、yaw（横の向き）と pitch（見下ろす角度）から見る位置にカメラを置く。全体が枠に収まる距離。 */
export function aim(cam, center, radius, yaw, pitch) {
  const fit = radius / Math.sin(THREE.MathUtils.degToRad(cam.fov / 2)) * (cam.aspect < 1 ? 1.15 / cam.aspect : 0.95);
  const dir = new THREE.Vector3(Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), Math.cos(yaw) * Math.cos(pitch));
  cam.position.copy(center).addScaledVector(dir, fit);
  cam.lookAt(center);
}

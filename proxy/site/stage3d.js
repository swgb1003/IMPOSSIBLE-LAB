// 試作品の 3D の共通部品：GLB の読み込み、研究所と同じ光、発明台の橙の輪、カメラの合わせ方。
// 1件のページ（回せる試作品）と一覧（ゆっくり回る小さな試作品）で使う。
import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { RoomEnvironment } from "three/addons/environments/RoomEnvironment.js";

export { THREE };

/** 描画装置。色の扱いと明るさは 1 件のページと一覧で同じにする。 */
export function createRenderer(options = {}) {
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, ...options });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.outputColorSpace = THREE.SRGBColorSpace;
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

/**
 * 試作品の GLB を読む。共有ページでは GLB を配れないので、文字にした GLB（window.ILAB_GLBS[key]）があればそれを使う。
 * 半透明の部品（覆いなど）は、中の部品が見えるように透かす。
 */
export function loadModel(url, key) {
  return new Promise((resolve, reject) => {
    const done = (gltf) => {
      gltf.scene.traverse((o) => {
        if (!o.isMesh) return;
        o.material = o.material.clone();
        if (o.material.transparent) { o.material.opacity = Math.min(o.material.opacity, 0.45); o.material.depthWrite = false; }
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

/** 試作品の中心を、yaw（横の向き）と pitch（見下ろす角度）から見る位置にカメラを置く。全体が枠に収まる距離。 */
export function aim(cam, center, radius, yaw, pitch) {
  const fit = radius / Math.sin(THREE.MathUtils.degToRad(cam.fov / 2)) * (cam.aspect < 1 ? 1.15 / cam.aspect : 0.95);
  const dir = new THREE.Vector3(Math.sin(yaw) * Math.cos(pitch), Math.sin(pitch), Math.cos(yaw) * Math.cos(pitch));
  cam.position.copy(center).addScaledVector(dir, fit);
  cam.lookAt(center);
}

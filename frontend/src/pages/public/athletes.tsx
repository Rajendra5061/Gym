/**
 * Athlete figures — solid silhouettes, one component per pose.
 *
 * Drawn rather than photographed: a gym's own photography is the only honest photography for its
 * own landing page, and a figure with no face, no skin tone and no body type excludes nobody.
 *
 * These are **filled silhouettes**, not line figures. A body outlined in a uniform thin stroke
 * reads as a stick figure however carefully it is posed, because what makes a silhouette look like
 * a person is mass: a thigh heavier than a calf, an upper arm heavier than a forearm, shoulders
 * wider than hips. So every limb here is a capsule with its own width, and the widths taper down
 * the chain.
 *
 * The geometry is computed rather than hand-written. Each limb is a chain of segments given as
 * (length, width, angle), and `Chain` walks it, placing each capsule at the joint the previous one
 * ended at. Hand-authored path data for a posed body is guesswork that cannot be adjusted — moving
 * one elbow means recomputing every coordinate after it. Here a pose is a list of angles, and
 * changing one changes exactly one joint.
 *
 * Angles are degrees, measured with **0 pointing straight down** and positive turning towards
 * screen-left, which matches SVG's own rotation so `Chain` and `endOf` cannot disagree.
 *
 * Every figure paints in `currentColor`, so the placement decides the colour. They are decorative —
 * the neighbouring copy always carries the meaning — so each is `aria-hidden`.
 */

import type { ReactNode } from 'react';

const RAD = Math.PI / 180;

/** Where a segment of `len` leaving (x, y) at `angle` ends up. Mirrors SVG's rotate() exactly. */
function endOf(x: number, y: number, len: number, angle: number): [number, number] {
  return [x - len * Math.sin(angle * RAD), y + len * Math.cos(angle * RAD)];
}

type Segment = { len: number; w: number; angle: number };

/** One limb bone: a capsule running from (x, y) along `angle`, rounded at both ends. */
function Bone({ x, y, len, w, angle }: Segment & { x: number; y: number }) {
  return (
    <g transform={`translate(${x} ${y}) rotate(${angle})`}>
      <rect x={-w / 2} y={-w / 2} width={w} height={len + w} rx={w / 2} />
    </g>
  );
}

/** A jointed limb. Each segment starts where the previous one ended. */
function Chain({ x, y, segments }: { x: number; y: number; segments: Segment[] }) {
  let cx = x;
  let cy = y;
  const bones: ReactNode[] = [];

  segments.forEach((segment, i) => {
    bones.push(<Bone key={i} x={cx} y={cy} {...segment} />);
    [cx, cy] = endOf(cx, cy, segment.len, segment.angle);
  });

  return <>{bones}</>;
}

type Torso = {
  headX: number; headY: number; headR?: number;
  torsoAngle?: number; torsoLen?: number; shoulderW?: number; hipW?: number;
};

/**
 * Where the limbs attach, derived from the same numbers that draw the torso.
 *
 * Every pose asks for these rather than guessing coordinates. A guessed hip is fine at thumbnail
 * size and falls apart the moment the figure is shown larger: the thigh starts a few pixels off
 * the pelvis, and in a one-colour silhouette that gap reads as a limb that has come off the body.
 * Deriving them means a pose cannot drift out of joint however far its lean is pushed.
 */
function anchors({ headX, headY, headR = 12, torsoAngle = 0, torsoLen = 40 }: Torso) {
  const [neckX, neckY] = endOf(headX, headY, headR + 4, torsoAngle);
  const [hipX, hipY] = endOf(neckX, neckY, torsoLen, torsoAngle);
  // Shoulders sit just below the neck, where an arm actually hangs from.
  const [shoulderX, shoulderY] = endOf(neckX, neckY, torsoLen * 0.22, torsoAngle);
  return { neckX, neckY, shoulderX, shoulderY, hipX, hipY };
}

/** Head, neck and torso — the mass every pose shares, so no pose can redraw it differently. */
function Body(props: Torso) {
  const { headX, headY, headR = 12, torsoAngle = 0, torsoLen = 40, shoulderW = 34, hipW = 26 } = props;
  const { neckX, neckY } = anchors(props);
  const [midX, midY] = endOf(neckX, neckY, torsoLen * 0.5, torsoAngle);

  return (
    <>
      <circle cx={headX} cy={headY} r={headR} />
      {/* Shoulders and hips are separate capsules; the taper between them is what reads as a
          torso rather than as a box. They overlap by design so the join never shows. */}
      <Bone x={neckX} y={neckY} len={torsoLen * 0.6} w={shoulderW} angle={torsoAngle} />
      <Bone x={midX} y={midY} len={torsoLen * 0.5} w={hipW} angle={torsoAngle} />
    </>
  );
}

type AthleteProps = { className?: string };

/** Wrapper: one box, one fill rule, one accessibility decision for the whole set. */
function Figure({ className, children }: AthleteProps & { children: ReactNode }) {
  return (
    <svg
      className={className}
      viewBox="0 0 120 150"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
      focusable="false"
      fill="currentColor"
    >
      {children}
    </svg>
  );
}

/** A loaded barbell drawn across (x, y). Slightly transparent so the body stays the subject. */
function Barbell({ x, y, width = 96 }: { x: number; y: number; width?: number }) {
  const half = width / 2;
  return (
    <g opacity=".85">
      <rect x={x - half} y={y - 3.5} width={width} height="7" rx="3.5" />
      <rect x={x - half - 4} y={y - 15} width="11" height="30" rx="4" />
      <rect x={x + half - 7} y={y - 15} width="11" height="30" rx="4" />
      <rect x={x - half - 13} y={y - 10} width="9" height="20" rx="3.5" />
      <rect x={x + half + 4} y={y - 10} width="9" height="20" rx="3.5" />
    </g>
  );
}

/* ---------------------------------------------------------------- the poses */

const CURL_TORSO: Torso = { headX: 60, headY: 26, shoulderW: 30 };
const CURL = anchors(CURL_TORSO);

/** Standing biceps curl, both arms drawn up to the shoulders. */
export function AthleteCurl({ className }: AthleteProps) {
  return (
    <Figure className={className}>
      <Body {...CURL_TORSO} />
      {/* Legs: thigh heavy, calf lighter, a touch of stance width. */}
      <Chain x={CURL.hipX - 7} y={CURL.hipY} segments={[{ len: 28, w: 19, angle: 6 }, { len: 28, w: 13, angle: 1 }]} />
      <Chain x={CURL.hipX + 7} y={CURL.hipY} segments={[{ len: 28, w: 19, angle: -6 }, { len: 28, w: 13, angle: -1 }]} />
      {/* Arms: elbows driven out and away from the ribs, forearms rising outside the torso.
          In a one-colour silhouette an arm is only visible where it is not overlapping the body,
          so the elbow has to clear the torso outline entirely — tuck it against the ribs, as an
          actual curl would, and arm and chest fuse into a single shapeless mass. */}
      <Chain x={45} y={50} segments={[{ len: 24, w: 14, angle: 26 }, { len: 26, w: 11, angle: 164 }]} />
      <Chain x={75} y={50} segments={[{ len: 24, w: 14, angle: -26 }, { len: 26, w: 11, angle: -164 }]} />
      {/* Dumbbells at the hands, well outside the chest. */}
      <g opacity=".9">
        <rect x="22" y="36" width="10" height="28" rx="4.5" />
        <rect x="88" y="36" width="10" height="28" rx="4.5" />
      </g>
    </Figure>
  );
}

const SQUAT_TORSO: Torso = { headX: 58, headY: 34, torsoAngle: -14 };
const SQUAT = anchors(SQUAT_TORSO);

/** Back squat at depth: thigh near horizontal, shin near vertical. */
export function AthleteSquat({ className }: AthleteProps) {
  return (
    <Figure className={className}>
      <Body {...SQUAT_TORSO} />
      {/* The horizontal thigh is the whole silhouette. Slope it downwards and the pose stops
          being a squat and becomes a stance with the feet apart. */}
      <Chain x={SQUAT.hipX} y={SQUAT.hipY} segments={[{ len: 26, w: 20, angle: 78 }, { len: 30, w: 14, angle: 4 }]} />
      <Chain x={SQUAT.hipX + 5} y={SQUAT.hipY} segments={[{ len: 26, w: 20, angle: -74 }, { len: 30, w: 14, angle: -4 }]} />
      {/* Arms out to the bar, elbows high. */}
      <Chain x={SQUAT.shoulderX - 9} y={SQUAT.shoulderY} segments={[{ len: 20, w: 14, angle: 104 }, { len: 16, w: 11, angle: 168 }]} />
      <Chain x={SQUAT.shoulderX + 9} y={SQUAT.shoulderY} segments={[{ len: 20, w: 14, angle: -100 }, { len: 16, w: 11, angle: -168 }]} />
      <Barbell x={SQUAT.neckX + 2} y={SQUAT.neckY - 4} width={86} />
    </Figure>
  );
}

/**
 * Kettlebell swing caught at the top, arms extended in front.
 *
 * The top of the swing, not the bottom of the hinge: at the bottom the torso is close to
 * horizontal, and a wide torso capsule lying on a diagonal reads as a blob at this size no matter
 * how the limbs are arranged. Upright body plus a bell held out in front is unmistakable.
 */
export function AthleteKettlebell({ className }: AthleteProps) {
  return (
    <Figure className={className}>
      <Body headX={42} headY={28} shoulderW={30} />
      {/* Braced stance: legs near vertical, feet a little apart. */}
      <Chain x={35} y={80} segments={[{ len: 28, w: 19, angle: 8 }, { len: 28, w: 13, angle: 2 }]} />
      <Chain x={49} y={80} segments={[{ len: 28, w: 19, angle: -8 }, { len: 28, w: 13, angle: -2 }]} />
      {/* Both arms straight out in front, holding the bell at shoulder height. */}
      <Chain x={46} y={52} segments={[{ len: 24, w: 14, angle: -86 }, { len: 20, w: 11, angle: -92 }]} />
      {/* The bell: handle first, then the body hanging just below the hands. */}
      <g opacity=".92">
        <path d="M88 52a9 8 0 0 1 18 0" fill="none" stroke="currentColor" strokeWidth="6" />
        <path d="M97 57c10 0 16 7 16 15s-7 13-16 13-16-5-16-13 6-15 16-15Z" />
      </g>
    </Figure>
  );
}

/** Mid-stride run, front knee driven up, trailing leg extended. */
export function AthleteRun({ className }: AthleteProps) {
  const torso: Torso = { headX: 68, headY: 28, torsoAngle: 14, torsoLen: 42 };
  const { shoulderX, shoulderY, hipX, hipY } = anchors(torso);

  return (
    <Figure className={className}>
      <Body {...torso} />
      {/* Front leg: knee high and forward, heel tucked under. */}
      <Chain x={hipX + 3} y={hipY} segments={[{ len: 26, w: 20, angle: -58 }, { len: 28, w: 14, angle: 14 }]} />
      {/* Trailing leg: long, driving back and down to the toe. */}
      <Chain x={hipX - 3} y={hipY} segments={[{ len: 28, w: 20, angle: 44 }, { len: 28, w: 14, angle: 84 }]} />
      {/* Arms counter-swinging the legs. */}
      <Chain x={shoulderX + 6} y={shoulderY} segments={[{ len: 22, w: 14, angle: -62 }, { len: 22, w: 11, angle: 18 }]} />
      <Chain x={shoulderX - 6} y={shoulderY} segments={[{ len: 22, w: 14, angle: 66 }, { len: 22, w: 11, angle: -16 }]} />
    </Figure>
  );
}

/** Overhead side stretch, one leg stepped forward. */
export function AthleteStretch({ className }: AthleteProps) {
  return (
    <Figure className={className}>
      <Body headX={54} headY={34} torsoAngle={-12} />
      <Chain x={54} y={82} segments={[{ len: 28, w: 19, angle: 14 }, { len: 28, w: 13, angle: 4 }]} />
      <Chain x={68} y={82} segments={[{ len: 28, w: 19, angle: -20 }, { len: 28, w: 13, angle: -8 }]} />
      {/* Both arms reaching up and over to the same side. */}
      <Chain x={48} y={52} segments={[{ len: 24, w: 14, angle: 168 }, { len: 24, w: 11, angle: 148 }]} />
      <Chain x={70} y={50} segments={[{ len: 24, w: 14, angle: -170 }, { len: 24, w: 11, angle: 160 }]} />
    </Figure>
  );
}

/** Pull-up at the top of the rep, chin over the bar. */
export function AthletePullUp({ className }: AthleteProps) {
  return (
    <Figure className={className}>
      {/* The bar and its uprights, behind the body. */}
      <g opacity=".8">
        <rect x="8" y="14" width="104" height="8" rx="4" />
        <rect x="10" y="0" width="8" height="18" rx="3" />
        <rect x="102" y="0" width="8" height="18" rx="3" />
      </g>
      <Body headX={60} headY={48} />
      {/* Arms up to the bar, elbows bent at the top of the pull. */}
      <Chain x={46} y={54} segments={[{ len: 22, w: 14, angle: 150 }, { len: 22, w: 11, angle: -170 }]} />
      <Chain x={74} y={54} segments={[{ len: 22, w: 14, angle: -150 }, { len: 22, w: 11, angle: 170 }]} />
      {/* Legs hanging, knees softly bent and ankles crossed. */}
      <Chain x={54} y={98} segments={[{ len: 26, w: 19, angle: 10 }, { len: 26, w: 13, angle: -18 }]} />
      <Chain x={66} y={98} segments={[{ len: 26, w: 19, angle: -10 }, { len: 26, w: 13, angle: 18 }]} />
    </Figure>
  );
}

/**
 * The whole set, plus the gradient each one is presented on. The gradients are the existing token
 * palette rather than new colours, so the band belongs to the rest of the product.
 */
export const ATHLETE_POSES = [
  { key: 'curl', label: 'Strength', grad: 'var(--grad-blue)', Art: AthleteCurl },
  { key: 'squat', label: 'Power', grad: 'var(--grad-orange)', Art: AthleteSquat },
  { key: 'kettlebell', label: 'Conditioning', grad: 'var(--grad-hero)', Art: AthleteKettlebell },
  { key: 'run', label: 'Cardio', grad: 'var(--grad-green)', Art: AthleteRun },
  { key: 'stretch', label: 'Mobility', grad: 'var(--grad-cyan)', Art: AthleteStretch },
  { key: 'pullup', label: 'Calisthenics', grad: 'var(--grad-blue)', Art: AthletePullUp },
] as const;

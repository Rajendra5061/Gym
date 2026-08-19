/**
 * Vector artwork for the public screens.
 *
 * Inline SVG rather than image files, for three reasons that matter here:
 *
 *  1. Nothing to fetch. The landing page is the first thing a member sees on a phone at the front
 *     desk, and artwork that arrives with the markup cannot be the request that fails or the box
 *     that pops the layout once it finally loads.
 *  2. It stays sharp. These are drawn, not sampled, so there is no retina variant to ship and no
 *     blur at any size.
 *  3. It follows the theme. The pieces painted on a card use `currentColor` and inherit; the pieces
 *     painted on the hero or auth gradients use literal translucent white, because those gradients
 *     stay vivid in both light and dark and everything on top of them is white on purpose.
 *
 * Every export is decorative — the meaning is always carried by adjacent text — so each carries
 * `aria-hidden` and is skipped by a screen reader rather than announced as a nameless graphic.
 */

/** Shared props. `className` is how each piece is sized and placed by the stylesheet. */
type ArtProps = { className?: string };

/* ------------------------------------------------------------------ hero art
   An app-shaped composition rather than a drawn athlete: cards, a bar chart, a progress ring and
   a dumbbell. It says "this is the console your gym runs on" without a stock-photo gym in sight,
   and it degrades gracefully when the column narrows because nothing depends on fine detail. */
export function HeroArt({ className }: ArtProps) {
  return (
    <svg
      className={className}
      viewBox="0 0 440 360"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
      focusable="false"
    >
      {/* Soft glow behind the stack, so the white line work has something to sit against. */}
      <circle cx="238" cy="168" r="150" fill="#ffffff" opacity=".07" />
      <circle cx="238" cy="168" r="104" fill="#ffffff" opacity=".05" />

      {/* Main panel: the members list. */}
      <g>
        <rect x="60" y="54" width="286" height="212" rx="18" fill="#ffffff" opacity=".16" />
        <rect
          x="60.5" y="54.5" width="285" height="211" rx="17.5"
          stroke="#ffffff" strokeOpacity=".38"
        />

        {/* Title bar with the three window dots. */}
        <rect x="80" y="76" width="96" height="9" rx="4.5" fill="#ffffff" opacity=".72" />
        <circle cx="312" cy="80" r="4" fill="#ffffff" opacity=".45" />
        <circle cx="298" cy="80" r="4" fill="#ffffff" opacity=".3" />
        <circle cx="284" cy="80" r="4" fill="#ffffff" opacity=".3" />

        {/* Member rows: avatar disc, name line, and a status pill. */}
        {[0, 1, 2].map((row) => {
          const y = 108 + row * 44;
          return (
            <g key={row}>
              <circle cx="94" cy={y + 13} r="13" fill="#ffffff" opacity={0.42 - row * 0.08} />
              <rect
                x="118" y={y + 5} width={122 - row * 22} height="8" rx="4"
                fill="#ffffff" opacity={0.62 - row * 0.12}
              />
              <rect
                x="118" y={y + 19} width={78 - row * 14} height="6" rx="3"
                fill="#ffffff" opacity={0.34 - row * 0.06}
              />
              <rect
                x="272" y={y + 7} width="52" height="14" rx="7"
                fill="#ffffff" opacity={0.3 - row * 0.06}
              />
            </g>
          );
        })}
      </g>

      {/* Floating card, lower left: the week's attendance as a small bar chart. */}
      <g>
        <rect x="24" y="212" width="168" height="112" rx="16" fill="#ffffff" opacity=".2" />
        <rect
          x="24.5" y="212.5" width="167" height="111" rx="15.5"
          stroke="#ffffff" strokeOpacity=".45"
        />
        <rect x="44" y="232" width="62" height="7" rx="3.5" fill="#ffffff" opacity=".6" />

        {/* Bars, rising left to right so the shape reads as growth at a glance. */}
        {[26, 40, 33, 54, 47, 66].map((height, i) => (
          <rect
            key={i}
            x={44 + i * 21}
            y={302 - height}
            width="12"
            rx="5"
            height={height}
            fill="#ffffff"
            opacity={0.42 + i * 0.07}
          />
        ))}
      </g>

      {/* Floating badge, upper right: a progress ring at roughly three-quarters. */}
      <g>
        <circle cx="366" cy="96" r="46" fill="#ffffff" opacity=".2" />
        <circle cx="366" cy="96" r="45.5" stroke="#ffffff" strokeOpacity=".45" />
        <circle cx="366" cy="96" r="29" stroke="#ffffff" strokeOpacity=".28" strokeWidth="7" />
        {/* 0.74 of the circumference, rotated to start at twelve o'clock. */}
        <circle
          cx="366" cy="96" r="29"
          stroke="#ffffff" strokeOpacity=".92" strokeWidth="7" strokeLinecap="round"
          strokeDasharray="135 182" transform="rotate(-90 366 96)"
        />
      </g>

      {/* The dumbbell, bottom right — the one literal object in the composition. */}
      <g opacity=".95">
        <rect x="252" y="288" width="112" height="12" rx="6" fill="#ffffff" opacity=".55" />
        <rect x="236" y="276" width="20" height="36" rx="7" fill="#ffffff" opacity=".82" />
        <rect x="360" y="276" width="20" height="36" rx="7" fill="#ffffff" opacity=".82" />
        <rect x="222" y="284" width="12" height="20" rx="5" fill="#ffffff" opacity=".6" />
        <rect x="382" y="284" width="12" height="20" rx="5" fill="#ffffff" opacity=".6" />
      </g>
    </svg>
  );
}

/* ------------------------------------------------------------- admin sign-in
   A console with a shield over it. The side panel is a dark gradient in both themes, so this is
   literal translucent white for the same reason the hero art is. */
export function AdminArt({ className }: ArtProps) {
  return (
    <svg
      className={className}
      viewBox="0 0 300 200"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
      focusable="false"
    >
      <circle cx="150" cy="100" r="88" fill="#ffffff" opacity=".05" />

      {/* The table behind: header strip plus rows, the shape of every admin screen. */}
      <rect x="42" y="34" width="216" height="130" rx="14" fill="#ffffff" opacity=".12" />
      <rect
        x="42.5" y="34.5" width="215" height="129" rx="13.5"
        stroke="#ffffff" strokeOpacity=".3"
      />
      <rect x="42" y="34" width="216" height="26" rx="13" fill="#ffffff" opacity=".14" />
      <rect x="60" y="43" width="66" height="8" rx="4" fill="#ffffff" opacity=".55" />

      {[0, 1, 2, 3].map((row) => (
        <g key={row}>
          <rect
            x="60" y={74 + row * 21} width={104 - row * 16} height="7" rx="3.5"
            fill="#ffffff" opacity={0.44 - row * 0.07}
          />
          <rect
            x="196" y={74 + row * 21} width="44" height="7" rx="3.5"
            fill="#ffffff" opacity={0.26 - row * 0.04}
          />
        </g>
      ))}

      {/* Shield, overlapping the table's corner: the permission gate in front of the data. */}
      <g transform="translate(196 104)">
        <path
          d="M34 2 60 13v22c0 18-11 30-26 37-15-7-26-19-26-37V13L34 2Z"
          fill="#ffffff" fillOpacity=".9"
        />
        <path
          d="m24 35 8 8 15-16"
          stroke="#1e1b3a" strokeWidth="4.5" strokeLinecap="round" strokeLinejoin="round"
        />
      </g>
    </svg>
  );
}

/* ------------------------------------------------------------ member sign-in
   The member's own view: a progress ring for the membership, and a check-in streak. */
export function MemberArt({ className }: ArtProps) {
  return (
    <svg
      className={className}
      viewBox="0 0 300 200"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
      focusable="false"
    >
      <circle cx="150" cy="100" r="88" fill="#ffffff" opacity=".05" />

      {/* Membership ring, a little over half elapsed. */}
      <g transform="translate(56 40)">
        <circle cx="60" cy="60" r="52" fill="#ffffff" opacity=".1" />
        <circle cx="60" cy="60" r="41" stroke="#ffffff" strokeOpacity=".24" strokeWidth="9" />
        <circle
          cx="60" cy="60" r="41"
          stroke="#ffffff" strokeOpacity=".92" strokeWidth="9" strokeLinecap="round"
          strokeDasharray="158 258" transform="rotate(-90 60 60)"
        />
        {/* A small dumbbell at the centre of the ring. */}
        <rect x="44" y="56" width="32" height="8" rx="4" fill="#ffffff" opacity=".8" />
        <rect x="36" y="50" width="11" height="20" rx="4" fill="#ffffff" opacity=".95" />
        <rect x="73" y="50" width="11" height="20" rx="4" fill="#ffffff" opacity=".95" />
      </g>

      {/* Check-in streak: a fortnight of visits, the filled ones read as days attended. */}
      <g transform="translate(190 56)">
        <rect x="0" y="0" width="76" height="88" rx="12" fill="#ffffff" opacity=".12" />
        <rect x=".5" y=".5" width="75" height="87" rx="11.5" stroke="#ffffff" strokeOpacity=".3" />
        <rect x="14" y="14" width="34" height="6" rx="3" fill="#ffffff" opacity=".5" />
        {[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11].map((cell) => {
          const filled = [0, 1, 3, 4, 5, 7, 8, 10].includes(cell);
          return (
            <rect
              key={cell}
              x={14 + (cell % 4) * 14}
              y={32 + Math.floor(cell / 4) * 14}
              width="10" height="10" rx="3"
              fill="#ffffff"
              opacity={filled ? 0.85 : 0.22}
            />
          );
        })}
      </g>
    </svg>
  );
}

/* ------------------------------------------------------------------- step art
   These three sit on cards rather than on a gradient, so they inherit `currentColor` and change
   with the theme along with everything else on the card. */

/** Step one: a member record being created. */
export function StepJoinArt({ className }: ArtProps) {
  return (
    <svg
      className={className} viewBox="0 0 120 96" fill="none"
      xmlns="http://www.w3.org/2000/svg" aria-hidden="true" focusable="false"
    >
      <rect x="18" y="14" width="84" height="68" rx="12" fill="currentColor" opacity=".1" />
      <rect
        x="18.5" y="14.5" width="83" height="67" rx="11.5"
        stroke="currentColor" strokeOpacity=".3"
      />
      <circle cx="44" cy="42" r="13" fill="currentColor" opacity=".55" />
      <path
        d="M28 70c2-9 8-14 16-14s14 5 16 14"
        stroke="currentColor" strokeOpacity=".45" strokeWidth="4" strokeLinecap="round"
      />
      <rect x="68" y="34" width="24" height="6" rx="3" fill="currentColor" opacity=".4" />
      <rect x="68" y="46" width="18" height="6" rx="3" fill="currentColor" opacity=".28" />
      <circle cx="94" cy="70" r="13" fill="currentColor" opacity=".9" />
      <path
        d="M88 70h12M94 64v12"
        stroke="var(--card)" strokeWidth="3.4" strokeLinecap="round"
      />
    </svg>
  );
}

/** Step two: the day's check-ins. */
export function StepTrainArt({ className }: ArtProps) {
  return (
    <svg
      className={className} viewBox="0 0 120 96" fill="none"
      xmlns="http://www.w3.org/2000/svg" aria-hidden="true" focusable="false"
    >
      <rect x="14" y="18" width="92" height="60" rx="12" fill="currentColor" opacity=".1" />
      <rect
        x="14.5" y="18.5" width="91" height="59" rx="11.5"
        stroke="currentColor" strokeOpacity=".3"
      />
      {[0, 1, 2, 3, 4].map((i) => (
        <rect
          key={i}
          x={28 + i * 13} y={62 - [16, 26, 20, 34, 28][i]}
          width="8" rx="4" height={[16, 26, 20, 34, 28][i]}
          fill="currentColor" opacity={0.35 + i * 0.11}
        />
      ))}
      <path
        d="M28 34c8-6 16 4 24-2s16 2 24-6"
        stroke="currentColor" strokeOpacity=".55" strokeWidth="3" strokeLinecap="round"
      />
    </svg>
  );
}

/** Step three: the receipt that follows a settled payment. */
export function StepTrackArt({ className }: ArtProps) {
  return (
    <svg
      className={className} viewBox="0 0 120 96" fill="none"
      xmlns="http://www.w3.org/2000/svg" aria-hidden="true" focusable="false"
    >
      <path
        d="M30 12h44a6 6 0 0 1 6 6v66l-9-6-8 6-8-6-8 6-9-6V18a6 6 0 0 1 6-6Z"
        fill="currentColor" fillOpacity=".1" stroke="currentColor" strokeOpacity=".3"
      />
      <rect x="40" y="28" width="30" height="6" rx="3" fill="currentColor" opacity=".45" />
      <rect x="40" y="42" width="22" height="5" rx="2.5" fill="currentColor" opacity=".3" />
      <rect x="40" y="54" width="26" height="5" rx="2.5" fill="currentColor" opacity=".3" />
      <circle cx="86" cy="66" r="17" fill="currentColor" opacity=".9" />
      <path
        d="m79 66 5 5 10-11"
        stroke="var(--card)" strokeWidth="3.6" strokeLinecap="round" strokeLinejoin="round"
      />
    </svg>
  );
}

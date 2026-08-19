import type { SVGProps } from 'react';

/**
 * Inline duotone stroke icons. Kept local rather than pulled from a package so the bundle has no
 * icon dependency and every glyph inherits currentColor. Each icon layers a soft `currentColor`
 * fill (the `duo` props) under crisp strokes, so the set reads richer than flat outlines while
 * still following whatever colour the surrounding chip or text gives it.
 */
type IconProps = SVGProps<SVGSVGElement> & { size?: number };

/** The soft under-layer every duotone icon shares. */
const duo = { fill: 'currentColor', stroke: 'none', opacity: 0.16 } as const;

function Svg({ size = 16, children, ...rest }: IconProps & { children: React.ReactNode }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...rest}
    >
      {children}
    </svg>
  );
}

export const IconDumbbell = (p: IconProps) => (
  <Svg {...p}>
    <rect x="5" y="6" width="3" height="12" rx="1.2" {...duo} />
    <rect x="16" y="6" width="3" height="12" rx="1.2" {...duo} />
    <path d="M6.5 6.5v11M3 9v5M17.5 6.5v11M21 9v5M6.5 12h11" />
  </Svg>
);
export const IconDashboard = (p: IconProps) => (
  <Svg {...p}>
    <rect x="3" y="3" width="7" height="9" rx="1" {...duo} opacity=".28" />
    <rect x="14" y="3" width="7" height="5" rx="1" {...duo} />
    <rect x="14" y="12" width="7" height="9" rx="1" {...duo} />
    <rect x="3" y="16" width="7" height="5" rx="1" {...duo} />
    <rect x="3" y="3" width="7" height="9" rx="1" /><rect x="14" y="3" width="7" height="5" rx="1" /><rect x="14" y="12" width="7" height="9" rx="1" /><rect x="3" y="16" width="7" height="5" rx="1" />
  </Svg>
);
export const IconUsers = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="9" cy="7" r="4" {...duo} opacity=".22" />
    <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2z" {...duo} />
    <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" />
  </Svg>
);
export const IconUser = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="12" cy="7" r="4" {...duo} opacity=".22" />
    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2z" {...duo} />
    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" /><circle cx="12" cy="7" r="4" />
  </Svg>
);
export const IconCrown = (p: IconProps) => (
  <Svg {...p}>
    <path d="M3 7l4 4 5-6 5 6 4-4v11H3z" {...duo} opacity=".2" />
    <path d="M3 7l4 4 5-6 5 6 4-4v11H3z" />
    <path d="M12 13.5h.01" />
  </Svg>
);
export const IconCard = (p: IconProps) => (
  <Svg {...p}>
    <rect x="2" y="5" width="20" height="14" rx="2" {...duo} opacity=".12" />
    <path d="M2 10h20v-3a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2z" {...duo} opacity=".26" />
    <rect x="2" y="5" width="20" height="14" rx="2" /><path d="M2 10h20" />
    <path d="M6 15h4" />
  </Svg>
);
export const IconCalendar = (p: IconProps) => (
  <Svg {...p}>
    <path d="M3 10h18v-4a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2z" {...duo} opacity=".24" />
    <rect x="3" y="4" width="18" height="18" rx="2" /><path d="M16 2v4M8 2v4M3 10h18" />
    <path d="M8 14.5h.01M12 14.5h.01M16 14.5h.01M8 18h.01M12 18h.01" />
  </Svg>
);
export const IconCheckSquare = (p: IconProps) => (
  <Svg {...p}>
    <rect x="3" y="3" width="18" height="18" rx="2" {...duo} opacity=".14" />
    <path d="M9 11l3 3L22 4" /><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11" />
  </Svg>
);
export const IconChart = (p: IconProps) => (
  <Svg {...p}>
    <path d="M3 3v18h18" />
    <rect x="7" y="12" width="3" height="6" fill="currentColor" fillOpacity=".18" />
    <rect x="12" y="8" width="3" height="10" fill="currentColor" fillOpacity=".28" />
    <rect x="17" y="4" width="3" height="14" fill="currentColor" fillOpacity=".4" />
  </Svg>
);
export const IconBell = (p: IconProps) => (
  <Svg {...p}>
    <path d="M18 8a6 6 0 1 0-12 0c0 7-3 9-3 9h18s-3-2-3-9" {...duo} opacity=".2" />
    <path d="M18 8a6 6 0 1 0-12 0c0 7-3 9-3 9h18s-3-2-3-9" /><path d="M13.7 21a2 2 0 0 1-3.4 0" />
  </Svg>
);
export const IconShield = (p: IconProps) => (
  <Svg {...p}>
    <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" {...duo} opacity=".18" />
    <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
    <path d="M9 11.5l2 2 4-4.5" />
  </Svg>
);
export const IconMenu = (p: IconProps) => (
  <Svg {...p}><path d="M3 6h18" /><path d="M3 12h18" /><path d="M3 18h18" /></Svg>
);
/** Double chevron pointing left; rotate it 180° for the expand direction. */
export const IconChevronsLeft = (p: IconProps) => (
  <Svg {...p}><polyline points="11 17 6 12 11 7" /><polyline points="18 17 13 12 18 7" /></Svg>
);
export const IconFile = (p: IconProps) => (
  <Svg {...p}>
    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" {...duo} opacity=".14" />
    <path d="M14 2v6h6" {...duo} opacity=".3" />
    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><path d="M14 2v6h6" />
    <path d="M9 13h6M9 17h4" />
  </Svg>
);
export const IconTrash = (p: IconProps) => (
  <Svg {...p}>
    <path d="M5 6h14l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2z" {...duo} opacity=".16" />
    <path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
    <path d="M10 11v6M14 11v6" />
  </Svg>
);
export const IconSettings = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="3" {...duo} opacity=".3" />
    <circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1A1.7 1.7 0 0 0 9 19.4a1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1A1.7 1.7 0 0 0 4.6 9a1.7 1.7 0 0 0-.3-1.9l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.9V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z" />
  </Svg>
);
export const IconLogout = (p: IconProps) => (
  <Svg {...p}><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" /><path d="M16 17l5-5-5-5M21 12H9" /></Svg>
);
export const IconSearch = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="11" cy="11" r="8" {...duo} opacity=".12" />
    <circle cx="11" cy="11" r="8" /><path d="M21 21l-4.3-4.3" />
  </Svg>
);
export const IconPlus = (p: IconProps) => (
  <Svg {...p}><path d="M12 5v14M5 12h14" /></Svg>
);
export const IconRefresh = (p: IconProps) => (
  <Svg {...p}><path d="M21 12a9 9 0 1 1-3-6.7L21 8" /><path d="M21 3v5h-5" /></Svg>
);
export const IconFilter = (p: IconProps) => (
  <Svg {...p}>
    <path d="M22 3H2l8 9.5V19l4 2v-8.5z" {...duo} opacity=".18" />
    <path d="M22 3H2l8 9.5V19l4 2v-8.5z" />
  </Svg>
);
export const IconPhone = (p: IconProps) => (
  <Svg {...p}>
    <path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8.1 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z" {...duo} opacity=".16" />
    <path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8.1 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z" />
  </Svg>
);
export const IconMapPin = (p: IconProps) => (
  <Svg {...p}>
    <path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0z" {...duo} opacity=".16" />
    <path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0z" />
    <circle cx="12" cy="10" r="3" />
  </Svg>
);
export const IconMail = (p: IconProps) => (
  <Svg {...p}>
    <rect x="2" y="4" width="20" height="16" rx="2" {...duo} opacity=".14" />
    <path d="M22 6l-10 7L2 6v-2h20z" {...duo} opacity=".2" />
    <rect x="2" y="4" width="20" height="16" rx="2" /><path d="M22 6l-10 7L2 6" />
  </Svg>
);
export const IconLock = (p: IconProps) => (
  <Svg {...p}>
    <rect x="3" y="11" width="18" height="11" rx="2" {...duo} opacity=".18" />
    <rect x="3" y="11" width="18" height="11" rx="2" /><path d="M7 11V7a5 5 0 0 1 10 0v4" />
    <path d="M12 15.5v2" />
  </Svg>
);
export const IconEye = (p: IconProps) => (
  <Svg {...p}>
    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" {...duo} opacity=".1" />
    <circle cx="12" cy="12" r="3" {...duo} opacity=".3" />
    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" /><circle cx="12" cy="12" r="3" />
  </Svg>
);
export const IconEyeOff = (p: IconProps) => (
  <Svg {...p}><path d="M17.9 17.9A10.7 10.7 0 0 1 12 20c-7 0-11-8-11-8a19.4 19.4 0 0 1 5.1-6M9.9 4.2A10.9 10.9 0 0 1 12 4c7 0 11 8 11 8a19.4 19.4 0 0 1-2.2 3.2M1 1l22 22M9.9 9.9a3 3 0 0 0 4.2 4.2" /></Svg>
);
export const IconMoney = (p: IconProps) => (
  <Svg {...p}>
    <rect x="2" y="6" width="20" height="12" rx="2" {...duo} opacity=".14" />
    <circle cx="12" cy="12" r="2.5" {...duo} opacity=".35" />
    <rect x="2" y="6" width="20" height="12" rx="2" /><circle cx="12" cy="12" r="2.5" /><path d="M6 12h.01M18 12h.01" />
  </Svg>
);
export const IconClock = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" {...duo} opacity=".14" />
    <circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" />
  </Svg>
);
export const IconEdit = (p: IconProps) => (
  <Svg {...p}>
    <path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z" {...duo} opacity=".18" />
    <path d="M12 20h9" /><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z" />
  </Svg>
);
export const IconInfo = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" {...duo} opacity=".14" />
    <circle cx="12" cy="12" r="9" /><path d="M12 16v-4M12 8h.01" />
  </Svg>
);
export const IconCheck = (p: IconProps) => (
  <Svg {...p}><path d="M20 6L9 17l-5-5" /></Svg>
);
export const IconArrowLeft = (p: IconProps) => (
  <Svg {...p}><path d="M19 12H5M12 19l-7-7 7-7" /></Svg>
);
export const IconArrowRight = (p: IconProps) => (
  <Svg {...p}><path d="M5 12h14M12 5l7 7-7 7" /></Svg>
);
export const IconDownload = (p: IconProps) => (
  <Svg {...p}>
    <path d="M7 10l5 5 5-5z" {...duo} opacity=".2" />
    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><path d="M7 10l5 5 5-5M12 15V3" />
  </Svg>
);
export const IconMessage = (p: IconProps) => (
  <Svg {...p}>
    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" {...duo} opacity=".16" />
    <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
    <path d="M8 10h.01M12 10h.01M16 10h.01" />
  </Svg>
);
export const IconBox = (p: IconProps) => (
  <Svg {...p}>
    <path d="M3.3 7L12 12l8.7-5L12 2z" {...duo} opacity=".24" />
    <path d="M12 12v10l9-5V7z" {...duo} opacity=".12" />
    <path d="M21 16V8l-9-5-9 5v8l9 5z" /><path d="M3.3 7L12 12l8.7-5M12 22V12" />
  </Svg>
);
export const IconQr = (p: IconProps) => (
  <Svg {...p}>
    <rect x="3" y="3" width="7" height="7" {...duo} opacity=".2" />
    <rect x="14" y="3" width="7" height="7" {...duo} opacity=".2" />
    <rect x="3" y="14" width="7" height="7" {...duo} opacity=".2" />
    <rect x="3" y="3" width="7" height="7" /><rect x="14" y="3" width="7" height="7" /><rect x="3" y="14" width="7" height="7" /><path d="M14 14h3v3h-3zM19 19h2v2h-2z" />
  </Svg>
);
export const IconWarning = (p: IconProps) => (
  <Svg {...p}>
    <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" {...duo} opacity=".18" />
    <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" /><path d="M12 9v4M12 17h.01" />
  </Svg>
);
export const IconSun = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="4" {...duo} opacity=".3" />
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
  </Svg>
);
export const IconMoon = (p: IconProps) => (
  <Svg {...p}>
    <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" {...duo} opacity=".2" />
    <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
  </Svg>
);

/**
 * Member communications — the direct messages members receive (payment receipts, renewal
 * receipts, diet plans, streak milestones, birthday and festival wishes) plus the tracking
 * counters behind them. Every shape mirrors a DTO under /api/communications; nothing is
 * invented client-side.
 */
import { api } from '@/api/client';
import type { PagedResult } from '@/api/types';

/** The occasion names the API records; new server values still render via `words()`. */
export type CommunicationKind =
  | 'PaymentReceived' | 'RenewalPayment' | 'DietPlan' | 'StreakMilestone' | 'Birthday' | 'Festival';

export const COMMUNICATION_KINDS: CommunicationKind[] = [
  'PaymentReceived', 'RenewalPayment', 'DietPlan', 'StreakMilestone', 'Birthday', 'Festival',
];

/** The channel filter the history endpoint accepts. */
export type CommunicationChannel = 'email' | 'whatsapp';

export interface CommunicationLogDto {
  id: number;
  memberId: number;
  memberName: string;
  memberCode: string;
  kind: CommunicationKind;
  detail?: string | null;
  sentOnDate: string;
  createdAtUtc: string;
  emailSent: boolean;
  whatsAppSent: boolean;
}

/** Query string accepted by `GET /api/communications`. */
export interface CommunicationsQuery {
  pageNumber?: number;
  pageSize?: number;
  kind?: CommunicationKind | '';
  memberId?: number | '';
  channel?: CommunicationChannel | '';
  from?: string;
  to?: string;
}

/** `GET /api/communications/usage` — this month's and today's send volumes. */
export interface CommunicationUsageDto {
  monthEmails: number;
  monthWhatsApp: number;
  todayEmails: number;
  todayWhatsApp: number;
  monthMembersReached: number;
}

export interface ChannelStatusDto {
  channel: string;
  provider: string;
  enabled: boolean;
}

/** Which channels each occasion goes out on. */
export interface OccasionChannelsDto {
  kind: CommunicationKind;
  email: boolean;
  whatsApp: boolean;
}

export interface CommunicationChannelsDto {
  channels: ChannelStatusDto[];
  occasions: OccasionChannelsDto[];
}

/** A festival the server is configured to send greetings for. */
export interface FestivalDto {
  key: string;
  name: string;
  date: string;
  greeting?: string | null;
}

/** One counted stream of outbound messaging (Occasions, RenewalReminders, PayLinks, InApp). */
export interface TrackingStreamDto {
  stream: string;
  description: string;
  today: number;
  thisMonth: number;
  email: number;
  sms: number;
  whatsApp: number;
  inApp: number;
}

/** `GET /api/communications/tracking` — every automated message across the app, counted. */
export interface MessageTrackingDto {
  streams: TrackingStreamDto[];
  todayTotal: number;
  monthTotal: number;
}

/** `POST /api/communications/test-whatsapp` outcome. */
export interface TestWhatsAppResultDto {
  sent: boolean;
  provider: string;
  detail: string;
}

export const communicationsApi = {
  paged: (query: CommunicationsQuery, signal?: AbortSignal) =>
    api.get<PagedResult<CommunicationLogDto>>('/api/communications', { ...query }, signal),

  /** Everything ever sent to one member, newest first. */
  forMember: (memberId: number, signal?: AbortSignal) =>
    api.get<CommunicationLogDto[]>(`/api/communications/member/${memberId}`, undefined, signal),

  usage: (signal?: AbortSignal) =>
    api.get<CommunicationUsageDto>('/api/communications/usage', undefined, signal),

  channels: (signal?: AbortSignal) =>
    api.get<CommunicationChannelsDto>('/api/communications/channels', undefined, signal),

  festivals: (signal?: AbortSignal) =>
    api.get<FestivalDto[]>('/api/communications/festivals', undefined, signal),

  tracking: (signal?: AbortSignal) =>
    api.get<MessageTrackingDto>('/api/communications/tracking', undefined, signal),

  /** Sends today's birthday and festival wishes; resolves to the number of members reached. */
  sendWishes: () =>
    api.post<number>('/api/communications/send-wishes'),

  testWhatsApp: (phone: string) =>
    api.post<TestWhatsAppResultDto>('/api/communications/test-whatsapp', { phone }),
};

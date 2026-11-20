import { http } from './http'
import type {
  ActivityLog,
  AppSettings,
  Certificate,
  CertificateOperationResult,
  Dashboard,
  DeployResult,
  DnsChallenge,
  ImportResult,
  LoginResponse,
  NginxSite,
  NginxStatus,
  PagedLogs,
} from './types'

export const authApi = {
  async login(username: string, password: string) {
    const { data } = await http.post<LoginResponse>('/auth/login', { username, password })
    return data
  },
  async me() {
    const { data } = await http.get<{ username: string; isDefaultPassword: boolean }>('/auth/me')
    return data
  },
  async changePassword(currentPassword: string, newPassword: string) {
    const { data } = await http.post('/auth/change-password', { currentPassword, newPassword })
    return data
  },
}

export const dashboardApi = {
  async get() {
    const { data } = await http.get<Dashboard>('/dashboard')
    return data
  },
}

export interface CertificateQuery {
  search?: string
  status?: string
  autoRenew?: boolean
}

export const certificateApi = {
  async list(query: CertificateQuery = {}) {
    const { data } = await http.get<Certificate[]>('/certificates', { params: query })
    return data
  },
  async get(id: number) {
    const { data } = await http.get<Certificate>(`/certificates/${id}`)
    return data
  },
  async create(payload: Record<string, unknown>) {
    const { data } = await http.post<Certificate>('/certificates', payload)
    return data
  },
  async update(id: number, payload: Record<string, unknown>) {
    const { data } = await http.put<Certificate>(`/certificates/${id}`, payload)
    return data
  },
  async remove(id: number) {
    await http.delete(`/certificates/${id}`)
  },
  async renew(id: number, force = true) {
    const { data } = await http.post<CertificateOperationResult>(`/certificates/${id}/renew`, { force })
    return data
  },
  async prepareDnsChallenge(id: number) {
    const { data } = await http.post<DnsChallenge>(`/certificates/${id}/dns-challenge`)
    return data
  },
  async completeDnsChallenge(id: number) {
    const { data } = await http.post<CertificateOperationResult>(
      `/certificates/${id}/dns-challenge/complete`,
    )
    return data
  },
  async cancelDnsChallenge(id: number) {
    await http.delete(`/certificates/${id}/dns-challenge`)
  },
  async sync(id: number) {
    const { data } = await http.post<Certificate>(`/certificates/${id}/sync`)
    return data
  },
  async deploy(id: number, updateNginxConfig = true, reload = true) {
    const { data } = await http.post<DeployResult>(`/certificates/${id}/deploy`, {
      updateNginxConfig,
      reload,
    })
    return data
  },
  async importFromNginx() {
    const { data } = await http.post<ImportResult>('/certificates/import')
    return data
  },
  async syncAll() {
    const { data } = await http.post<{ changed: number }>('/certificates/sync-all')
    return data
  },
}

export const nginxApi = {
  async sites() {
    const { data } = await http.get<NginxSite[]>('/nginx/sites')
    return data
  },
  async scan() {
    const { data } = await http.post<{ sites: NginxSite[]; count: number; sslCount: number }>('/nginx/scan')
    return data
  },
  async status() {
    const { data } = await http.get<NginxStatus>('/nginx/status')
    return data
  },
  async test() {
    const { data } = await http.post<{ success: boolean; output: string }>('/nginx/test')
    return data
  },
  async reload() {
    const { data } = await http.post<{ success: boolean; output: string }>('/nginx/reload')
    return data
  },
  async config(path?: string) {
    const { data } = await http.get<{ path: string | null; content: string }>('/nginx/config', {
      params: path ? { path } : {},
    })
    return data
  },
  async importCertificates() {
    const { data } = await http.post<ImportResult>('/nginx/import')
    return data
  },
  async certificateSites(id: number) {
    const { data } = await http.get<NginxSite[]>(`/nginx/certificates/${id}/sites`)
    return data
  },
}

export interface LogQuery {
  page?: number
  pageSize?: number
  level?: string
  category?: string
  certificateId?: number
  search?: string
}

export const logApi = {
  async list(query: LogQuery = {}) {
    const { data } = await http.get<PagedLogs>('/logs', { params: query })
    return data
  },
  async clear() {
    await http.delete('/logs')
  },
}

export const settingsApi = {
  async get() {
    const { data } = await http.get<AppSettings>('/settings')
    return data
  },
  async update(payload: Partial<AppSettings>) {
    const { data } = await http.put<AppSettings>('/settings', payload)
    return data
  },
}

export type { ActivityLog, AppSettings, Certificate, Dashboard, NginxSite, NginxStatus }

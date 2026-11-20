import dayjs from 'dayjs'
import relativeTimePlugin from 'dayjs/plugin/relativeTime'
import 'dayjs/locale/zh-cn'

dayjs.extend(relativeTimePlugin)
dayjs.locale('zh-cn')

export function formatDate(value?: string | null, withTime = true): string {
  if (!value) return '—'
  return dayjs(value).format(withTime ? 'YYYY-MM-DD HH:mm' : 'YYYY-MM-DD')
}

export function relativeTime(value?: string | null): string {
  if (!value) return '—'
  return dayjs(value).fromNow()
}

export function daysLabel(days?: number | null): string {
  if (days === null || days === undefined) return '未知'
  if (days < 0) return `已过期 ${Math.abs(days)} 天`
  if (days === 0) return '今天到期'
  return `剩余 ${days} 天`
}

export function issuerName(issuer?: string | null): string {
  if (!issuer) return '未知'
  const cn = issuer.match(/CN=([^,]+)/)
  if (cn) return cn[1].replace(/^"|"$/g, '')
  return issuer
}

export interface StatusMeta {
  label: string
  type: 'success' | 'warning' | 'danger' | 'info' | 'primary'
  color: string
  description: string
}

export const statusMap: Record<string, StatusMeta> = {
  valid: { label: '有效', type: 'success', color: '#0f9f8f', description: '证书在有效期内' },
  expiring: { label: '即将到期', type: 'warning', color: '#e6a23c', description: '30 天内到期' },
  critical: { label: '紧急', type: 'danger', color: '#f56c6c', description: '7 天内到期' },
  expired: { label: '已过期', type: 'danger', color: '#c45656', description: '证书已过期' },
  pending: { label: '待签发', type: 'info', color: '#909399', description: '尚未签发证书' },
  issuing: { label: '签发中', type: 'primary', color: '#5b8ff9', description: '正在申请证书' },
  error: { label: '异常', type: 'danger', color: '#d9534f', description: '证书或部署异常' },
  unknown: { label: '未知', type: 'info', color: '#909399', description: '无法读取证书信息' },
}

export function statusMeta(status: string): StatusMeta {
  return statusMap[status] ?? statusMap.unknown
}

export function certProgress(cert: { notBefore?: string | null; notAfter?: string | null }): number {
  if (!cert.notBefore || !cert.notAfter) return 0
  const start = dayjs(cert.notBefore).valueOf()
  const end = dayjs(cert.notAfter).valueOf()
  if (end <= start) return 100
  return Math.min(100, Math.max(0, ((Date.now() - start) / (end - start)) * 100))
}

export function categoryLabel(category: string): string {
  const map: Record<string, string> = {
    issue: '签发',
    renew: '续期',
    deploy: '部署',
    scan: '扫描',
    nginx: 'Nginx',
    import: '导入',
    auth: '认证',
    system: '系统',
  }
  return map[category] ?? category
}

export function levelType(level: string): 'success' | 'warning' | 'danger' | 'info' {
  if (level === 'success') return 'success'
  if (level === 'warning') return 'warning'
  if (level === 'error') return 'danger'
  return 'info'
}

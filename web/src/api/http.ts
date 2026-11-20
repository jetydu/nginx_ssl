import axios from 'axios'

export const TOKEN_KEY = 'nsm_token'
export const USER_KEY = 'nsm_user'
export const DEFAULT_PASSWORD_KEY = 'nsm_default_password'

export const http = axios.create({
  baseURL: '/api',
  timeout: 180_000,
})

http.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_KEY)
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

http.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem(TOKEN_KEY)
      localStorage.removeItem(USER_KEY)
      if (!location.pathname.startsWith('/login')) {
        const redirect = encodeURIComponent(location.pathname + location.search)
        location.href = `/login?redirect=${redirect}`
      }
    }
    return Promise.reject(error)
  },
)

export function errorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as { message?: string; output?: string } | undefined
    if (data?.message) {
      return data.output && !data.message.includes(data.output)
        ? `${data.message}\n${data.output}`
        : data.message
    }
    if (error.code === 'ECONNABORTED') return '请求超时，请稍后重试'
    return error.message
  }
  return error instanceof Error ? error.message : String(error)
}

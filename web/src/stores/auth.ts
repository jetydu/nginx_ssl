import { defineStore } from 'pinia'
import { authApi } from '../api'
import { DEFAULT_PASSWORD_KEY, TOKEN_KEY, USER_KEY } from '../api/http'

export const useAuthStore = defineStore('auth', {
  state: () => ({
    token: localStorage.getItem(TOKEN_KEY) ?? '',
    username: localStorage.getItem(USER_KEY) ?? '',
    isDefaultPassword: localStorage.getItem(DEFAULT_PASSWORD_KEY) === '1',
    loading: false,
  }),
  getters: {
    isLoggedIn: (state) => Boolean(state.token),
  },
  actions: {
    async login(username: string, password: string) {
      this.loading = true
      try {
        const result = await authApi.login(username, password)
        this.token = result.token
        this.username = result.username
        this.isDefaultPassword = result.isDefaultPassword
        localStorage.setItem(TOKEN_KEY, result.token)
        localStorage.setItem(USER_KEY, result.username)
        localStorage.setItem(DEFAULT_PASSWORD_KEY, result.isDefaultPassword ? '1' : '0')
      } finally {
        this.loading = false
      }
    },
    async fetchMe() {
      const me = await authApi.me()
      this.username = me.username
      this.isDefaultPassword = me.isDefaultPassword
      localStorage.setItem(USER_KEY, me.username)
      localStorage.setItem(DEFAULT_PASSWORD_KEY, me.isDefaultPassword ? '1' : '0')
    },
    markPasswordChanged() {
      this.isDefaultPassword = false
      localStorage.setItem(DEFAULT_PASSWORD_KEY, '0')
    },
    logout() {
      this.token = ''
      this.username = ''
      this.isDefaultPassword = false
      localStorage.removeItem(TOKEN_KEY)
      localStorage.removeItem(USER_KEY)
      localStorage.removeItem(DEFAULT_PASSWORD_KEY)
    },
  },
})

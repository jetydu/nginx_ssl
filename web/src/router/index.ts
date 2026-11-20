import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/LoginView.vue'),
      meta: { public: true, title: '登录' },
    },
    {
      path: '/',
      component: () => import('../layout/AppLayout.vue'),
      children: [
        {
          path: '',
          name: 'dashboard',
          component: () => import('../views/DashboardView.vue'),
          meta: { title: '证书总览' },
        },
        {
          path: 'certificates',
          name: 'certificates',
          component: () => import('../views/CertificatesView.vue'),
          meta: { title: '证书管理' },
        },
        {
          path: 'nginx',
          name: 'nginx',
          component: () => import('../views/NginxView.vue'),
          meta: { title: 'Nginx 站点' },
        },
        {
          path: 'logs',
          name: 'logs',
          component: () => import('../views/LogsView.vue'),
          meta: { title: '操作日志' },
        },
        {
          path: 'settings',
          name: 'settings',
          component: () => import('../views/SettingsView.vue'),
          meta: { title: '系统设置' },
        },
      ],
    },
    {
      path: '/:pathMatch(.*)*',
      redirect: '/',
    },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  const title = to.meta.title ? `${to.meta.title} · Nginx SSL Manager` : 'Nginx SSL Manager'
  document.title = title

  if (to.meta.public) {
    if (to.name === 'login' && auth.token) {
      return { path: '/' }
    }
    return true
  }

  if (!auth.token) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }

  if (!auth.username) {
    try {
      await auth.fetchMe()
    } catch {
      auth.logout()
      return { path: '/login', query: { redirect: to.fullPath } }
    }
  }

  return true
})

export default router

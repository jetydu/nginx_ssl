<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import {
  ArrowDown,
  Connection,
  Document,
  Expand,
  Fold,
  Odometer,
  Setting,
  SwitchButton,
  Tickets,
  WarningFilled,
} from '@element-plus/icons-vue'
import { useAuthStore } from '../stores/auth'
import { nginxApi } from '../api'
import type { NginxStatus } from '../api/types'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const collapsed = ref(false)
const nginx = ref<NginxStatus | null>(null)
const statusError = ref(false)
let timer: number | undefined

const pageTitle = computed(() => (route.meta.title as string) || '控制台')
const nginxHealthy = computed(() => nginx.value?.running && nginx.value?.configOk)

async function loadStatus() {
  try {
    nginx.value = await nginxApi.status()
    statusError.value = false
  } catch {
    statusError.value = true
  }
}

async function logout() {
  try {
    await ElMessageBox.confirm('确定要退出当前登录吗？', '退出登录', {
      confirmButtonText: '退出',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }
  auth.logout()
  await router.push('/login')
}

function handleCommand(command: string) {
  if (command === 'settings') router.push('/settings')
  if (command === 'logout') logout()
}

onMounted(() => {
  loadStatus()
  timer = window.setInterval(loadStatus, 45_000)
})

onBeforeUnmount(() => {
  if (timer) window.clearInterval(timer)
})
</script>

<template>
  <div class="shell" :class="{ 'is-collapsed': collapsed }">
    <aside class="sidebar">
      <div class="brand">
        <div class="brand-icon">
          <svg viewBox="0 0 24 24" width="22" height="22" fill="none">
            <path
              d="M12 2.5 4.8 5.4v6.1c0 4.7 3 8.4 7.2 10 4.2-1.6 7.2-5.3 7.2-10V5.4L12 2.5Z"
              stroke="currentColor"
              stroke-width="1.7"
              stroke-linejoin="round"
            />
            <path d="m8.7 11.8 2.2 2.2 4.4-4.4" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
        </div>
        <div v-show="!collapsed" class="brand-text">
          <strong>SSL Manager</strong>
          <span>Nginx 证书控制台</span>
        </div>
      </div>

      <el-menu
        :default-active="route.path"
        :collapse="collapsed"
        :collapse-transition="false"
        router
        class="side-menu"
      >
        <el-menu-item index="/">
          <el-icon><Odometer /></el-icon>
          <template #title>证书总览</template>
        </el-menu-item>
        <el-menu-item index="/certificates">
          <el-icon><Document /></el-icon>
          <template #title>证书管理</template>
        </el-menu-item>
        <el-menu-item index="/nginx">
          <el-icon><Connection /></el-icon>
          <template #title>Nginx 站点</template>
        </el-menu-item>
        <el-menu-item index="/logs">
          <el-icon><Tickets /></el-icon>
          <template #title>操作日志</template>
        </el-menu-item>
        <el-menu-item index="/settings">
          <el-icon><Setting /></el-icon>
          <template #title>系统设置</template>
        </el-menu-item>
      </el-menu>

      <div class="sidebar-bottom">
        <div v-show="!collapsed" class="side-note">
          <span class="side-note-title">自动续期守护中</span>
          <span class="side-note-text">到期前自动检查并续签证书</span>
        </div>
        <button class="collapse-btn" type="button" @click="collapsed = !collapsed">
          <el-icon><Expand v-if="collapsed" /><Fold v-else /></el-icon>
        </button>
      </div>
    </aside>

    <div class="main">
      <header class="topbar">
        <div class="topbar-left">
          <span class="crumb">控制台</span>
          <span class="crumb-sep">/</span>
          <strong>{{ pageTitle }}</strong>
        </div>

        <div class="topbar-right">
          <button class="nginx-pill" type="button" @click="router.push('/nginx')">
            <span class="pulse" :class="{ ok: nginxHealthy, bad: !nginxHealthy }" />
            <span v-if="statusError">Nginx 状态未知</span>
            <span v-else-if="!nginx">检测中…</span>
            <span v-else-if="nginx.running && nginx.configOk">Nginx 运行正常</span>
            <span v-else-if="nginx.running">配置检查异常</span>
            <span v-else>Nginx 未运行</span>
          </button>

          <el-dropdown trigger="click" @command="handleCommand">
            <button class="user-button" type="button">
              <span class="avatar">{{ (auth.username || 'A').slice(0, 1).toUpperCase() }}</span>
              <span class="user-name">{{ auth.username || 'admin' }}</span>
              <el-icon class="chev"><ArrowDown /></el-icon>
            </button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="settings">
                  <el-icon><Setting /></el-icon>系统设置
                </el-dropdown-item>
                <el-dropdown-item command="logout" divided>
                  <el-icon><SwitchButton /></el-icon>退出登录
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </header>

      <div v-if="auth.isDefaultPassword" class="password-banner">
        <el-icon><WarningFilled /></el-icon>
        <span>当前仍在使用默认密码 <b>admin123</b>，存在安全风险。</span>
        <router-link to="/settings">立即修改</router-link>
      </div>

      <main class="content">
        <router-view v-slot="{ Component }">
          <transition name="fade" mode="out-in">
            <component :is="Component" :key="route.path" />
          </transition>
        </router-view>
      </main>
    </div>
  </div>
</template>

<style scoped>
.shell {
  min-height: 100vh;
  display: flex;
  background: var(--nsm-bg);
}

.sidebar {
  width: 236px;
  flex: 0 0 auto;
  min-height: 100vh;
  position: sticky;
  top: 0;
  align-self: flex-start;
  display: flex;
  flex-direction: column;
  padding: 18px 14px 16px;
  color: #c8d4e4;
  background:
    radial-gradient(340px 220px at 20% -10%, rgba(15, 159, 143, 0.25), transparent 70%),
    linear-gradient(180deg, #0b1220 0%, #0d1728 100%);
  border-right: 1px solid rgba(255, 255, 255, 0.04);
  transition: width 0.2s ease;
  z-index: 10;
}

.is-collapsed .sidebar {
  width: 78px;
  padding-left: 12px;
  padding-right: 12px;
}

.brand {
  display: flex;
  align-items: center;
  gap: 11px;
  padding: 4px 6px 20px;
  min-height: 58px;
}

.brand-icon {
  width: 38px;
  height: 38px;
  flex: 0 0 auto;
  display: grid;
  place-items: center;
  border-radius: 11px;
  color: #7ff0df;
  background: rgba(127, 240, 223, 0.09);
  border: 1px solid rgba(127, 240, 223, 0.18);
}

.brand-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  overflow: hidden;
}

.brand-text strong {
  font-size: 15px;
  color: #f2f7f7;
  letter-spacing: -0.01em;
  white-space: nowrap;
}

.brand-text span {
  font-size: 11px;
  color: #74849c;
  white-space: nowrap;
}

.side-menu {
  border-right: none;
  background: transparent;
  --el-menu-bg-color: transparent;
  --el-menu-text-color: #9fb0c7;
  --el-menu-hover-bg-color: rgba(255, 255, 255, 0.05);
  --el-menu-active-color: #ffffff;
  --el-menu-item-height: 44px;
  --el-menu-base-level-padding: 12px;
  --el-menu-icon-width: 20px;
}

.side-menu :deep(.el-menu-item) {
  margin: 4px 0;
  border-radius: 10px;
  font-size: 13.5px;
}

.side-menu :deep(.el-menu-item.is-active) {
  background: linear-gradient(90deg, rgba(15, 159, 143, 0.34), rgba(15, 159, 143, 0.12));
  box-shadow: inset 0 0 0 1px rgba(111, 227, 210, 0.14);
  color: #ecfffc;
}

.side-menu :deep(.el-menu-item.is-active .el-icon) {
  color: #7ff0df;
}

.sidebar-bottom {
  margin-top: auto;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.side-note {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 13px;
  border-radius: 12px;
  background: rgba(15, 159, 143, 0.09);
  border: 1px solid rgba(111, 227, 210, 0.12);
}

.side-note-title {
  font-size: 12px;
  font-weight: 600;
  color: #8ff1e2;
}

.side-note-text {
  font-size: 11px;
  line-height: 1.5;
  color: #6f8299;
}

.collapse-btn {
  height: 34px;
  border-radius: 9px;
  border: 1px solid rgba(255, 255, 255, 0.06);
  background: rgba(255, 255, 255, 0.03);
  color: #74849c;
  cursor: pointer;
  display: grid;
  place-items: center;
  transition: all 0.15s ease;
}

.collapse-btn:hover {
  color: #d9e5f3;
  background: rgba(255, 255, 255, 0.07);
}

.main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.topbar {
  height: 68px;
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 0 28px;
  background: rgba(255, 255, 255, 0.78);
  border-bottom: 1px solid var(--nsm-border);
  backdrop-filter: blur(14px);
  position: sticky;
  top: 0;
  z-index: 9;
}

.topbar-left {
  display: flex;
  align-items: center;
  gap: 9px;
  font-size: 13px;
  color: var(--nsm-muted);
}

.topbar-left strong {
  color: var(--nsm-text);
  font-size: 14px;
  font-weight: 600;
}

.crumb-sep {
  color: #c6ced9;
}

.topbar-right {
  display: flex;
  align-items: center;
  gap: 14px;
}

.nginx-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  height: 34px;
  padding: 0 13px;
  border-radius: 999px;
  border: 1px solid var(--nsm-border);
  background: #fff;
  color: #475467;
  font-size: 12px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.nginx-pill:hover {
  border-color: #bcded9;
  background: #f6fbfa;
}

.pulse {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #98a2b3;
  position: relative;
}

.pulse.ok {
  background: #12b76a;
  box-shadow: 0 0 0 4px rgba(18, 183, 106, 0.12);
}

.pulse.bad {
  background: #f04438;
  box-shadow: 0 0 0 4px rgba(240, 68, 56, 0.12);
}

.user-button {
  display: flex;
  align-items: center;
  gap: 9px;
  border: none;
  background: transparent;
  cursor: pointer;
  padding: 4px 2px;
  color: var(--nsm-text);
}

.avatar {
  width: 32px;
  height: 32px;
  border-radius: 10px;
  display: grid;
  place-items: center;
  font-size: 13px;
  font-weight: 700;
  color: #0b7f73;
  background: var(--nsm-brand-soft);
  border: 1px solid #cceee9;
}

.user-name {
  font-size: 13px;
  font-weight: 500;
}

.chev {
  font-size: 12px;
  color: #98a2b3;
}

.password-banner {
  display: flex;
  align-items: center;
  gap: 9px;
  margin: 16px 28px 0;
  padding: 10px 14px;
  border-radius: 10px;
  background: #fff7e8;
  border: 1px solid #f8dfb2;
  color: #8a5a13;
  font-size: 12.5px;
}

.password-banner a {
  margin-left: auto;
  font-weight: 600;
  color: #b76e00;
}

.content {
  padding: 24px 28px 40px;
  width: 100%;
  max-width: 1480px;
  margin: 0 auto;
  flex: 1;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.16s ease, transform 0.16s ease;
}

.fade-enter-from {
  opacity: 0;
  transform: translateY(4px);
}

.fade-leave-to {
  opacity: 0;
}

@media (max-width: 900px) {
  .sidebar {
    width: 78px;
    padding-left: 12px;
    padding-right: 12px;
  }

  .brand-text,
  .side-note,
  .user-name {
    display: none !important;
  }

  .topbar {
    padding: 0 16px;
  }

  .content {
    padding: 18px 16px 32px;
  }

  .password-banner {
    margin: 14px 16px 0;
  }

  .nginx-pill span:last-child {
    display: none;
  }
}
</style>

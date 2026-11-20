<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { Lock, User } from '@element-plus/icons-vue'
import { useAuthStore } from '../stores/auth'
import { errorMessage } from '../api/http'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const formRef = ref<FormInstance>()

const form = reactive({
  username: 'admin',
  password: '',
})

const rules: FormRules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码', trigger: 'blur' }],
}

async function submit() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  try {
    await auth.login(form.username, form.password)
    ElMessage.success('欢迎回来')
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.push(redirect)
  } catch (error) {
    ElMessage.error(errorMessage(error))
  }
}
</script>

<template>
  <div class="login-page">
    <section class="login-brand">
      <div class="brand-mark">
        <svg viewBox="0 0 24 24" width="30" height="30" fill="none">
          <path
            d="M12 2.5 4.8 5.4v6.1c0 4.7 3 8.4 7.2 10 4.2-1.6 7.2-5.3 7.2-10V5.4L12 2.5Z"
            stroke="currentColor"
            stroke-width="1.6"
            stroke-linejoin="round"
          />
          <path d="m8.7 11.8 2.2 2.2 4.4-4.4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </div>

      <div class="brand-copy">
        <span class="eyebrow">NGINX · LET'S ENCRYPT</span>
        <h1>把证书续期<br />交给系统自动完成</h1>
        <p>集中管理 Nginx 的 SSL 证书，监控到期时间，自动申请、自动部署、自动重载，让 HTTPS 永远在线。</p>
      </div>

      <ul class="feature-list">
        <li><span class="tick">✓</span> 自动读取 nginx 配置并发现已有证书</li>
        <li><span class="tick">✓</span> 到期前自动通过 ACME HTTP-01 续期</li>
        <li><span class="tick">✓</span> 续期后校验配置并平滑重载 nginx</li>
      </ul>

      <div class="orb orb-one" />
      <div class="orb orb-two" />
      <div class="grid-lines" />
    </section>

    <section class="login-form-side">
      <div class="login-card">
        <div class="login-heading">
          <h2>登录控制台</h2>
          <p>使用管理员账户管理证书与 nginx</p>
        </div>

        <el-form ref="formRef" :model="form" :rules="rules" label-position="top" size="large" @submit.prevent="submit">
          <el-form-item label="用户名" prop="username">
            <el-input v-model="form.username" placeholder="admin" :prefix-icon="User" autocomplete="username" />
          </el-form-item>
          <el-form-item label="密码" prop="password">
            <el-input
              v-model="form.password"
              type="password"
              show-password
              placeholder="请输入管理员密码"
              :prefix-icon="Lock"
              autocomplete="current-password"
              @keyup.enter="submit"
            />
          </el-form-item>

          <el-button type="primary" size="large" class="submit-btn" :loading="auth.loading" @click="submit">
            登录控制台
          </el-button>
        </el-form>
      </div>

      <p class="login-footer">Nginx SSL Manager · .NET 10 + Vue 3</p>
    </section>
  </div>
</template>

<style scoped>
.login-page {
  min-height: 100vh;
  display: grid;
  grid-template-columns: minmax(420px, 1.1fr) minmax(420px, 0.9fr);
  background: #f6f8fb;
}

.login-brand {
  position: relative;
  overflow: hidden;
  padding: 56px 64px;
  display: flex;
  flex-direction: column;
  gap: 42px;
  color: #eaf6f4;
  background:
    radial-gradient(700px 500px at 80% 10%, rgba(15, 159, 143, 0.42), transparent 65%),
    linear-gradient(150deg, #071019 0%, #0b1a2a 55%, #0a2b2a 100%);
}

.brand-mark {
  width: 54px;
  height: 54px;
  display: grid;
  place-items: center;
  border-radius: 16px;
  color: #7ff0df;
  background: rgba(127, 240, 223, 0.1);
  border: 1px solid rgba(127, 240, 223, 0.22);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.28);
}

.brand-copy {
  position: relative;
  z-index: 2;
  max-width: 560px;
  margin-top: auto;
}

.eyebrow {
  font-size: 11px;
  letter-spacing: 0.22em;
  color: #6fe3d2;
  font-weight: 600;
}

.brand-copy h1 {
  margin: 18px 0 20px;
  font-size: clamp(32px, 3.4vw, 48px);
  line-height: 1.18;
  letter-spacing: -0.035em;
  font-weight: 700;
  color: #ffffff;
}

.brand-copy p {
  margin: 0;
  font-size: 15px;
  line-height: 1.8;
  color: rgba(227, 242, 240, 0.68);
  max-width: 460px;
}

.feature-list {
  position: relative;
  z-index: 2;
  list-style: none;
  margin: 0 0 auto;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 13px;
  font-size: 13.5px;
  color: rgba(227, 242, 240, 0.76);
}

.feature-list li {
  display: flex;
  align-items: center;
  gap: 10px;
}

.tick {
  width: 20px;
  height: 20px;
  display: grid;
  place-items: center;
  border-radius: 50%;
  font-size: 11px;
  color: #071019;
  background: #6fe3d2;
  flex: 0 0 auto;
}

.orb {
  position: absolute;
  border-radius: 50%;
  filter: blur(2px);
  opacity: 0.55;
}

.orb-one {
  width: 380px;
  height: 380px;
  right: -120px;
  top: 80px;
  background: radial-gradient(circle at 30% 30%, rgba(75, 222, 199, 0.34), rgba(11, 26, 42, 0) 68%);
  border: 1px solid rgba(111, 227, 210, 0.12);
}

.orb-two {
  width: 520px;
  height: 520px;
  right: -180px;
  bottom: -240px;
  background: radial-gradient(circle at 40% 40%, rgba(15, 159, 143, 0.28), rgba(11, 26, 42, 0) 70%);
  border: 1px solid rgba(111, 227, 210, 0.1);
}

.grid-lines {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(255, 255, 255, 0.028) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.028) 1px, transparent 1px);
  background-size: 56px 56px;
  mask-image: radial-gradient(circle at 30% 40%, black, transparent 78%);
  pointer-events: none;
}

.login-form-side {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px;
  gap: 28px;
}

.login-card {
  width: min(400px, 100%);
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid rgba(228, 233, 241, 0.9);
  border-radius: 22px;
  padding: 34px 34px 28px;
  box-shadow: 0 30px 70px rgba(15, 23, 42, 0.1);
  backdrop-filter: blur(12px);
}

.login-heading h2 {
  margin: 0;
  font-size: 24px;
  letter-spacing: -0.02em;
}

.login-heading p {
  margin: 8px 0 28px;
  color: var(--nsm-muted);
  font-size: 13px;
}

.submit-btn {
  width: 100%;
  margin-top: 6px;
  height: 44px;
  font-size: 14px;
  font-weight: 600;
}

.login-hint {
  margin-top: 22px;
  padding: 11px 13px;
  border-radius: 10px;
  background: #f0f9f7;
  border: 1px solid #d5efeb;
  color: #47706b;
  font-size: 12px;
  line-height: 1.6;
  display: flex;
  gap: 8px;
  align-items: flex-start;
}

.login-hint .el-icon {
  margin-top: 2px;
  color: var(--nsm-brand);
}

.login-footer {
  font-size: 12px;
  color: #98a2b3;
}

@media (max-width: 960px) {
  .login-page {
    grid-template-columns: 1fr;
  }

  .login-brand {
    display: none;
  }

  .login-form-side {
    min-height: 100vh;
    background:
      radial-gradient(600px 400px at 50% 0%, rgba(15, 159, 143, 0.15), transparent 70%),
      #f6f8fb;
    padding: 24px;
  }
}
</style>

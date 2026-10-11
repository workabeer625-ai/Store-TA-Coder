# دليل رفع المشروع إلى GitHub — Store-TA-Coder

المستودع: **https://github.com/workabeer625-ai/Store-TA-Coder**
الفرع الرئيسي: `main`

> هذا الملف مؤقت — بعد ما ينجح الرفع تقدر تحذفه (`git rm UPLOAD_GUIDE.md`).

---

## قبل أي شيء: تأكد من تثبيت Git

```bash
git --version
```

إذا ما كان مثبتاً: حمّله من https://git-scm.com/download/win وبعد التثبيت أعد تشغيل الطرفية.

---

## الطريقة 1 (الأسهل والموصى بها): نسخ المشروع داخل المستودع

> ميزتها: ما فيها أي تعارض في التاريخ، والملفات المهمة (`.gitignore` و `README.md`) تكون موجودة مسبقاً.

### Windows — PowerShell

```powershell
# 1) اذهب للمكان الذي تريد وضع المشروع فيه (مثلاً سطح المكتب)
cd $HOME\Desktop

# 2) حمّل المستودع الفارغ
git clone https://github.com/workabeer625-ai/Store-TA-Coder.git
cd Store-TA-Coder

# 3) انسخ كل مجلدات مشروعك إلى داخل مجلد Store-TA-Coder
#    (backend، frontend، mobile، ... إلخ)
#    لا تنسخ: node_modules  build  dist  .dart_tool  .next  venv  .venv  .env

# 4) تأكد مما سيتم رفعه (لازم ما تشوف node_modules أو .env في القائمة)
git status

# 5) سجّل الملفات
git add .
git commit -m "feat: upload full project (backend + frontend)"

# 6) ارفع
git push origin main
```

### macOS / Linux — Terminal

```bash
cd ~/Desktop
git clone https://github.com/workabeer625-ai/Store-TA-Coder.git
cd Store-TA-Coder

# انسخ ملفات مشروعك إلى هنا

git status
git add .
git commit -m "feat: upload full project (backend + frontend)"
git push origin main
```

---

## الطريقة 2: الرفع من داخل مجلد مشروعك الحالي (بدون نسخ)

استخدمها إذا مشروعك مجلد جاهز على جهازك وتريد رفعه من مكانه:

```powershell
# Windows
cd "C:\Users\اسمك\مسار\مشروعك"
```

```bash
# macOS / Linux
cd ~/path/to/your/project
```

ثم نفّذ نفس الأوامر في الحالتين:

```bash
git init
git remote add origin https://github.com/workabeer625-ai/Store-TA-Coder.git
git add .
git commit -m "feat: upload full project (backend + frontend)"
git branch -M main
git push -u origin main --force
```

> `--force` مطلوب هنا فقط لأن المستودع فيه commit قديم (README + .gitignore) بتاريخ مختلف عن مجلدك.
> آمن تماماً في حالتك لأن المستودع كان فارغاً.

---

## تأكد من وجود `.gitignore` قبل الرفع

ملف `.gitignore` موجود مسبقاً في المستودع (الطريقة 1) ويستثني تلقائياً:

- `node_modules/` — حزم Node
- `build/` و `.dart_tool/` — مخرجات Flutter/Dart
- `.venv/` و `__pycache__/` — بايثون
- `.env` — الأسرار ومفاتيح API
- `dist/` و `.next/` — مخرجات البناء

إذا استخدمت **الطريقة 2**، أنشئ الملف أولاً داخل مجلد مشروعك:

```bash
curl -o .gitignore https://raw.githubusercontent.com/workabeer625-ai/Store-TA-Coder/main/.gitignore
```

أو حمّله يدوياً من المستودع وضعه في جذر مشروعك.

**مهم:** لا ترفع أبداً ملفات `.env` أو مفاتيح `serviceAccountKey.json` — GitHub قد يعلّمها كتسريب أمان.

---

## المصادقة (إذا طلب منك اسم مستخدم وكلمة مرور)

GitHub **لم يعد يقبل كلمة مرور الحساب**. استخدم واحداً من هذين:

### خيار أ: Personal Access Token (الأسرع)

1. افتح: GitHub ← صورتك ← **Settings** ← **Developer settings** ← **Personal access tokens** ← **Tokens (classic)**
2. **Generate new token (classic)**
3. فعّل صلاحية `repo` فقط
4. انسخ الـ token (يبدأ بـ `ghp_`)
5. عند الرفع: اسم المستخدم = `workabeer625-ai`، وكلمة المرور = الـ token

### خيار ب: GitHub CLI

```bash
gh auth login
# اختر: GitHub.com → HTTPS → Login with a web browser
```

ثم نفّذ أوامر الرفع أعلاه بدون إدخال بيانات.

---

## التحقق من نجاح الرفع

افتح: **https://github.com/workabeer625-ai/Store-TA-Coder**

المفروض تشوف مجلداتك (backend، frontend، ...) بدل `README.md` فقط.

للتأكد من مجلد معين محلياً:

```bash
git ls-files | head -50
```

---

## حل المشاكل الشائعة

| المشكلة | الحل |
|---|---|
| `failed to push some refs` | شغّل `git pull origin main --rebase` ثم `git push origin main` |
| `src refspec main does not match` | شغّل `git branch -M main` ثم أعد المحاولة |
| `remote origin already exists` | شغّل `git remote set-url origin https://github.com/workabeer625-ai/Store-TA-Coder.git` |
| ملف أكبر من 100MB مرفوض | ثبّت Git LFS: `git lfs install` ثم `git lfs track "*.psd"` ثم أعد `git add .` |
| رفعت `.env` بالغلط | احذفه فوراً وغيّر المفاتيح: `git rm --cached .env` |

---

## بعد الرفع

قل لي "تم الرفع" وأنا أتكفّل بالباقي:

- أحذف الفرع المؤقت `arena/1df9ef1c-store-ta-coder`
- أدمج ملف `.gitignore` إذا احتجته
- أكتب لك `README.md` احترافي يشرح المشروع (backend + frontend + Flutter) وطريقة التشغيل

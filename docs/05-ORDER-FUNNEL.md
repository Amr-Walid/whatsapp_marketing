# 🛒 المرحلة 4: مسار الطلب (Order Funnel)

> مسارين: **الطلب داخل الشات** (Conversational Commerce) و **الطلب من صفحة الهبوط** (Landing Page)

---

## ⚠️ أهم فرق تقني عن الرسمي: الأزرار

### الحقيقة اللي لازم تعرفها

```
❌ في الطرق غير الرسمية:
   • الأزرار التفاعلية (Interactive Buttons)     → غير مستقرة / مش شغالة
   • القوائم المنسدلة (List Messages)            → بتظهر مرة وتختفي مرة
   • كتالوج المنتجات (Product Catalog)           → مفيش
   • WhatsApp Flows                              → مفيش

   حتى المكتبات اللي بتقول إنها بتدعمهم:
   - بتظهر على بعض الأجهزة وبعضها لأ
   - بتتعطل مع أي تحديث لواتساب
   - Baileys نفسه فيه issues مفتوحة (#2465) عن ده
```

### ✅ الحل: قوائم رقمية مصمّمة بعناية

قائمة رقمية مصممة كويس بتشتغل **أحسن من الأزرار** فعلياً، لأنها:
- بتشتغل على أي جهاز، أي إصدار
- العميل المصري متعوّد عليها (زي IVR التليفون)
- بتقلل الأخطاء لو صمّمتها صح

```
❌ التصميم السيء (بيربك العميل):
─────────────────────────────
اختر من القائمة:
1- تيشيرت رجالي كلاسيك أبيض قطن مصري 100% مقاسات متعددة
2- تيشيرت رجالي بولو أزرق قطن مخلوط مقاسات M L XL
3- تيشيرت حريمي كاجوال وردي
4- بنطلون جينز رجالي
5- ...
6- ...
7- ...
8- ...
9- ...
10- ...
11- ...

المشاكل: قائمة طويلة + نص كثيف + بدون أسعار + مربك


✅ التصميم الجيد:
─────────────────────────────
اختار من هنا 👇

*1* 👕 تيشيرتات — من 250ج
*2* 👖 بناطيل — من 450ج
*3* 👗 فساتين — من 600ج

*0* 🗣️ أكلّم موظف

_ابعت الرقم بس_

   ↓ العميل بعت "1"

👕 *التيشيرتات*

*1* كلاسيك أبيض — 250ج
*2* بولو أزرق — 320ج
*3* أوفرسايز أسود — 290ج

*9* ⬅️ رجوع
*0* 🗣️ موظف

المميزات: قصيرة + أسعار واضحة + رجوع + إيموجي مساعد
```

---

## 1. مسار الشات (Conversational Commerce)

### آلة الحالة (State Machine)

```javascript
const STATES = {
  IDLE:              'idle',
  MENU_MAIN:         'menu_main',
  MENU_CATEGORY:     'menu_category',
  PRODUCT_DETAIL:    'product_detail',
  ASK_VARIANT:       'ask_variant',      // مقاس/لون
  ASK_QUANTITY:      'ask_quantity',
  CART_REVIEW:       'cart_review',
  ASK_NAME:          'ask_name',
  ASK_GOVERNORATE:   'ask_governorate',
  ASK_ADDRESS:       'ask_address',
  ASK_PAYMENT:       'ask_payment',
  CONFIRM_ORDER:     'confirm_order',
  DONE:              'done',
  HUMAN_HANDOFF:     'human_handoff',
};
```

### تطبيق البوت

```javascript
class OrderBot {
  constructor(deps) { Object.assign(this, deps); }

  async handle(sessionId, phone, message) {
    // ═══ 0. أولوية قصوى: opt-out ═══
    if (isOptOut(message.text)) {
      await this.optOut(phone, sessionId);
      return;
    }

    // ═══ 1. حمّل/أنشئ المحادثة ═══
    let conv = await this.loadConv(phone, sessionId);

    // ═══ 2. تسليم بشري؟ ═══
    if (!conv.is_bot_active) {
      await this.forwardToChatwoot(conv, message);
      return;
    }

    const hand = await checkHandoff(conv, message.text);
    if (hand.handoff) {
      await doHandoff(conv, hand.reason, hand.urgent);
      return;
    }

    // ═══ 3. انتهاء الصلاحية (30 دقيقة سكوت) ═══
    if (conv.expires_at && new Date(conv.expires_at) < new Date()) {
      if (conv.state !== STATES.IDLE) {
        await this.reply(conv, 'المحادثة السابقة انتهت. نبدأ من جديد 👍');
      }
      conv = await this.resetConv(conv);
    }

    // ═══ 4. أوامر عامة (تعمل من أي حالة) ═══
    const global = await this.globalCommands(conv, message.text);
    if (global) return;

    // ═══ 5. الموجّه ═══
    const handler = this.handlers[conv.state] ?? this.handlers[STATES.IDLE];
    await handler.call(this, conv, message);

    await this.touch(conv);
  }

  // ═══════════════ الأوامر العامة ═══════════════
  async globalCommands(conv, text) {
    const t = normalizeArabic(text);

    // القائمة الرئيسية
    if (['قائمة','منيو','menu','ابدا','ابدأ','#'].some(k => t === k)) {
      await this.goto(conv, STATES.MENU_MAIN);
      return true;
    }
    // موظف
    if (t === '0' || ['موظف','انسان','حد'].some(k => t.includes(k))) {
      await doHandoff(conv, 'user_request');
      return true;
    }
    // إلغاء
    if (['الغاء','كانسل','cancel','بلاش'].some(k => t === k)) {
      await this.reply(conv, 'تم الإلغاء ✅ لو حبيت تبدأ من جديد ابعت *قائمة*');
      await this.resetConv(conv);
      return true;
    }
    // تتبع
    if (['تتبع','فين اوردري','الاوردر','طلبي'].some(k => t.includes(k))) {
      await this.trackOrder(conv);
      return true;
    }
    return false;
  }

  // ═══════════════ معالجات الحالات ═══════════════
  handlers = {

    // ── IDLE: أول تفاعل ──
    async [STATES.IDLE](conv, msg) {
      const t = normalizeArabic(msg.text);

      // نية إيجابية للرد على الحملة
      if (['ايوه','اه','نعم','تمام','ماشي','ابعت','yes','ok','اوك']
          .some(k => t === k || t.startsWith(k))) {
        // ✨ العميل مهتم — ابعت المنتج المرشح مباشرة
        const rec = await this.getRecommendation(conv.customer_id);
        if (rec) {
          await this.showProduct(conv, rec);
          return;
        }
      }

      // نية سؤال عن سعر
      if (['كام','بكام','السعر','سعر','price'].some(k => t.includes(k))) {
        await this.goto(conv, STATES.MENU_MAIN);
        return;
      }

      // ترحيب + قائمة
      const name = await this.customerName(conv.customer_id);
      await this.reply(conv, spin(`
{أهلاً|ازيك} ${name ? name + ' ' : ''}{👋|🌸}

{أنا هنا أساعدك|تحت أمرك}. {اختار|تقدر تختار} من هنا 👇
      `));
      await sleep(1200);
      await this.goto(conv, STATES.MENU_MAIN);
    },

    // ── القائمة الرئيسية ──
    async [STATES.MENU_MAIN](conv, msg) {
      const cats = await this.categories();
      const pick = parseInt(normalizeDigits(msg.text), 10);
      const cat = cats[pick - 1];

      if (!cat) {
        await this.fail(conv, 'اختار رقم من القائمة 👆');
        return;
      }

      await this.setContext(conv, { category: cat.id });
      await this.goto(conv, STATES.MENU_CATEGORY);
    },

    // ── قائمة الفئة ──
    async [STATES.MENU_CATEGORY](conv, msg) {
      const t = normalizeDigits(msg.text).trim();

      if (t === '9') { await this.goto(conv, STATES.MENU_MAIN); return; }

      const products = await this.productsIn(conv.context.category);
      const p = products[parseInt(t, 10) - 1];

      if (!p) { await this.fail(conv, 'اختار رقم من القائمة 👆 أو *9* للرجوع'); return; }

      await this.showProduct(conv, p);
    },

    // ── تفاصيل المنتج ──
    async [STATES.PRODUCT_DETAIL](conv, msg) {
      const t = normalizeDigits(msg.text).trim();

      if (t === '9') { await this.goto(conv, STATES.MENU_CATEGORY); return; }

      if (t === '1' || ['اطلب','عايزه','هاخده','تمام'].some(k =>
            normalizeArabic(msg.text).includes(k))) {
        const p = await this.product(conv.context.product_id);
        // في مقاسات/ألوان؟
        if (p.variants?.length) {
          await this.goto(conv, STATES.ASK_VARIANT);
        } else {
          await this.goto(conv, STATES.ASK_QUANTITY);
        }
        return;
      }

      await this.fail(conv, 'ابعت *1* للطلب أو *9* للرجوع');
    },

    // ── المقاس/اللون ──
    async [STATES.ASK_VARIANT](conv, msg) {
      const p = await this.product(conv.context.product_id);
      const t = normalizeDigits(msg.text).trim().toUpperCase();

      // بالرقم أو بالاسم
      let v = p.variants[parseInt(t, 10) - 1];
      if (!v) v = p.variants.find(x => x.name.toUpperCase() === t);

      if (!v) {
        await this.fail(conv,
          `اختار من:\n${p.variants.map((x,i) => `*${i+1}* ${x.name}`).join('\n')}`
        );
        return;
      }

      if (v.stock <= 0) {
        await this.reply(conv,
          `${v.name} خلصانة حالياً 😔\nمتاح: ${
            p.variants.filter(x => x.stock > 0).map(x => x.name).join(' · ')
          }`
        );
        return;
      }

      await this.setContext(conv, { variant_id: v.id, variant_name: v.name });
      await this.goto(conv, STATES.ASK_QUANTITY);
    },

    // ── الكمية ──
    async [STATES.ASK_QUANTITY](conv, msg) {
      const qty = parseInt(normalizeDigits(msg.text).replace(/\D/g, ''), 10);

      if (!qty || qty < 1) {
        await this.fail(conv, 'ابعت رقم الكمية (مثال: *2*)');
        return;
      }
      if (qty > 10) {
        await this.reply(conv,
          'للكميات الكبيرة (أكتر من 10) هوصلك بموظف يساعدك 👍'
        );
        await doHandoff(conv, 'bulk_order');
        return;
      }

      const p = await this.product(conv.context.product_id);
      if (qty > (p.stock ?? 999)) {
        await this.reply(conv, `المتاح حالياً ${p.stock} قطعة بس. تحب تاخد كام؟`);
        return;
      }

      // ضيف للسلة
      const cart = [...(conv.cart ?? []), {
        product_id:   p.id,
        product_name: p.name,
        variant_id:   conv.context.variant_id,
        variant_name: conv.context.variant_name,
        qty,
        unit_price:   p.price,
        line_total:   p.price * qty,
      }];
      await this.setCart(conv, cart);
      await this.goto(conv, STATES.CART_REVIEW);
    },

    // ── مراجعة السلة ──
    async [STATES.CART_REVIEW](conv, msg) {
      const t = normalizeDigits(msg.text).trim();

      if (t === '1') { await this.goto(conv, STATES.ASK_NAME); return; }
      if (t === '2') { await this.goto(conv, STATES.MENU_MAIN); return; }
      if (t === '3') { await this.setCart(conv, []);
                       await this.reply(conv, 'تم إفراغ السلة ✅');
                       await this.goto(conv, STATES.MENU_MAIN); return; }

      await this.fail(conv, 'ابعت *1* للمتابعة، *2* لإضافة منتج، *3* لإفراغ السلة');
    },

    // ── الاسم ──
    async [STATES.ASK_NAME](conv, msg) {
      const name = msg.text.trim();
      if (name.length < 3 || name.length > 60) {
        await this.fail(conv, 'اكتب اسمك بالكامل (زي: أحمد محمد علي)');
        return;
      }
      await this.setContext(conv, { name });
      await this.goto(conv, STATES.ASK_GOVERNORATE);
    },

    // ── المحافظة ──
    async [STATES.ASK_GOVERNORATE](conv, msg) {
      const govs = await this.governorates();
      const t = normalizeDigits(msg.text).trim();

      let g = govs[parseInt(t, 10) - 1];
      if (!g) {
        // بحث بالاسم
        const n = normalizeArabic(msg.text);
        g = govs.find(x => normalizeArabic(x.name).includes(n) ||
                           n.includes(normalizeArabic(x.name)));
      }

      if (!g) {
        await this.fail(conv, 'اختار رقم المحافظة من القائمة 👆');
        return;
      }

      await this.setContext(conv, {
        governorate: g.name,
        shipping: g.shipping_cost,
      });
      await this.goto(conv, STATES.ASK_ADDRESS);
    },

    // ── العنوان ──
    async [STATES.ASK_ADDRESS](conv, msg) {
      // موقع جغرافي؟
      if (msg.type === 'location') {
        await this.setContext(conv, {
          address: `📍 ${msg.latitude},${msg.longitude}`,
          has_pin: true,
        });
        await this.reply(conv,
          'استلمت الموقع 📍\nاكتب كمان: اسم الشارع + رقم العمارة + الدور'
        );
        return;
      }

      const addr = msg.text.trim();
      if (addr.length < 12) {
        await this.fail(conv, spin(`
{محتاج|عايز} العنوان {بالتفصيل|كامل} 🏠

{مثال|زي كده}: 15 شارع الجمهورية، الدور 3، شقة 8، بجانب صيدلية النور
        `));
        return;
      }

      const prev = conv.context.address ?? '';
      await this.setContext(conv, {
        address: prev ? `${prev}\n${addr}` : addr,
      });
      await this.goto(conv, STATES.ASK_PAYMENT);
    },

    // ── طريقة الدفع ──
    async [STATES.ASK_PAYMENT](conv, msg) {
      const t = normalizeDigits(msg.text).trim();
      const MAP = { '1': 'cod', '2': 'card', '3': 'wallet' };
      const pm = MAP[t];

      if (!pm) {
        await this.fail(conv, 'اختار *1* أو *2* أو *3*');
        return;
      }

      await this.setContext(conv, { payment_method: pm });
      await this.goto(conv, STATES.CONFIRM_ORDER);
    },

    // ── التأكيد النهائي ──
    async [STATES.CONFIRM_ORDER](conv, msg) {
      const t = normalizeDigits(msg.text).trim();
      const n = normalizeArabic(msg.text);

      if (t === '1' || ['اكد','تمام','ايوه','موافق','اه'].some(k => n.includes(k))) {
        await this.createOrder(conv);
        return;
      }
      if (t === '2' || ['غير','تعديل','عدل'].some(k => n.includes(k))) {
        await this.reply(conv, 'تحب تعدّل إيه؟\n*1* العنوان\n*2* الكمية\n*3* المنتجات');
        await this.setContext(conv, { editing: true });
        return;
      }
      if (t === '3' || ['الغاء','كانسل'].some(k => n.includes(k))) {
        await this.reply(conv, 'تم الإلغاء. لو احتجت حاجة ابعت *قائمة* 👍');
        await this.resetConv(conv);
        return;
      }

      await this.fail(conv, 'ابعت *1* للتأكيد، *2* للتعديل، *3* للإلغاء');
    },
  };

  // ═══════════════ عروض الشاشات ═══════════════

  async render(conv) {
    switch (conv.state) {

      case STATES.MENU_MAIN: {
        const cats = await this.categories();
        return this.reply(conv, `
${spin('{اختار|اختار من هنا|شوف اللي يعجبك}')} 👇

${cats.map((c, i) =>
  `*${i+1}* ${c.emoji} ${c.name} — من ${c.min_price}ج`
).join('\n')}

*0* 🗣️ أكلّم موظف

_ابعت الرقم بس_
        `);
      }

      case STATES.MENU_CATEGORY: {
        const cat = await this.category(conv.context.category);
        const ps = await this.productsIn(conv.context.category);
        return this.reply(conv, `
${cat.emoji} *${cat.name}*

${ps.map((p, i) =>
  `*${i+1}* ${p.name} — ${p.price}ج${p.stock <= 3 ? ' ⚡آخر قطع' : ''}`
).join('\n')}

*9* ⬅️ رجوع  ·  *0* 🗣️ موظف
        `);
      }

      case STATES.ASK_VARIANT: {
        const p = await this.product(conv.context.product_id);
        const avail = p.variants.filter(v => v.stock > 0);
        return this.reply(conv, `
اختار ${p.variant_label ?? 'المقاس'} 📏

${avail.map((v, i) => `*${i+1}* ${v.name}`).join('\n')}

*9* ⬅️ رجوع
        `);
      }

      case STATES.ASK_QUANTITY:
        return this.reply(conv, 'عايز كام قطعة؟ 🔢\n_(ابعت الرقم)_');

      case STATES.CART_REVIEW: {
        const cart = conv.cart ?? [];
        const sub = cart.reduce((s, i) => s + i.line_total, 0);
        return this.reply(conv, `
🛒 *سلة الطلب*
━━━━━━━━━━━━━━━
${cart.map(i =>
  `• ${i.product_name}${i.variant_name ? ` (${i.variant_name})` : ''}
  ${i.qty} × ${i.unit_price}ج = *${i.line_total}ج*`
).join('\n')}
━━━━━━━━━━━━━━━
المجموع: *${sub}ج*
_(الشحن يُحسب بعد المحافظة)_

*1* ✅ متابعة الطلب
*2* ➕ إضافة منتج
*3* 🗑️ إفراغ السلة
        `);
      }

      case STATES.ASK_NAME:
        return this.reply(conv, 'اسمك بالكامل؟ 📝');

      case STATES.ASK_GOVERNORATE: {
        const govs = await this.governorates();
        return this.reply(conv, `
المحافظة؟ 🏙️

${govs.map((g, i) => `*${i+1}* ${g.name}`).join('\n')}

_(أو اكتب اسم المحافظة)_
        `);
      }

      case STATES.ASK_ADDRESS:
        return this.reply(conv, `
العنوان بالتفصيل؟ 🏠

_مثال: 15 ش الجمهورية، الدور 3، شقة 8، جانب صيدلية النور_

💡 تقدر كمان تبعت الموقع من 📎 → Location
        `);

      case STATES.ASK_PAYMENT: {
        const ctx = conv.context;
        const sub = (conv.cart ?? []).reduce((s, i) => s + i.line_total, 0);
        return this.reply(conv, `
💳 طريقة الدفع؟

*1* 💵 كاش عند الاستلام (${ctx.shipping}ج شحن)
*2* 💳 بطاقة أونلاين (شحن مجاني ✨)
*3* 📱 محفظة إلكترونية (فودافون كاش)
        `);
      }

      case STATES.CONFIRM_ORDER: {
        const c = conv.context;
        const cart = conv.cart ?? [];
        const sub = cart.reduce((s, i) => s + i.line_total, 0);
        const ship = c.payment_method === 'card' ? 0 : c.shipping;
        const total = sub + ship;
        await this.setContext(conv, { total, shipping_final: ship });

        return this.reply(conv, `
📋 *مراجعة الطلب*
━━━━━━━━━━━━━━━━━
${cart.map(i =>
  `• ${i.product_name}${i.variant_name ? ` (${i.variant_name})` : ''} ×${i.qty} = ${i.line_total}ج`
).join('\n')}
━━━━━━━━━━━━━━━━━
المنتجات: ${sub}ج
الشحن: ${ship === 0 ? 'مجاني ✨' : ship + 'ج'}
*الإجمالي: ${total}ج*
━━━━━━━━━━━━━━━━━
👤 ${c.name}
📱 ${conv.phone}
📍 ${c.governorate}
🏠 ${c.address}
💳 ${{cod:'كاش عند الاستلام', card:'بطاقة', wallet:'محفظة'}[c.payment_method]}
━━━━━━━━━━━━━━━━━

*1* ✅ تأكيد الطلب
*2* ✏️ تعديل
*3* ❌ إلغاء
        `);
      }
    }
  }

  // ═══════════════ إنشاء الأوردر ═══════════════

  async createOrder(conv) {
    const c = conv.context;
    const cart = conv.cart ?? [];

    // 🔒 قفل لمنع الأوردر المكرر
    const lock = await acquireLock(`order:${conv.phone}`, 30_000);
    if (!lock) {
      await this.reply(conv, 'ثانية واحدة، بأسجّل طلبك... ⏳');
      return;
    }

    try {
      // تحقق من المخزون تاني (ممكن نفد في الوقت اللي فات)
      for (const item of cart) {
        const stock = await this.checkStock(item.product_id, item.variant_id);
        if (stock < item.qty) {
          await this.reply(conv, `
😔 للأسف *${item.product_name}* المتاح منه ${stock} بس دلوقتي.

تحب:
*1* آخد المتاح (${stock})
*2* أشيله من الطلب
*0* أكلّم موظف
          `);
          return;
        }
      }

      const orderNumber = await this.nextOrderNumber();

      const order = await this.db.one(`
        INSERT INTO orders
          (order_number, customer_id, session_id, channel, items,
           subtotal, shipping, total, customer_name, phone,
           address, governorate, payment_method, status)
        VALUES ($1,$2,$3,'whatsapp_bot',$4,$5,$6,$7,$8,$9,$10,$11,$12,'new')
        RETURNING *
      `, [
        orderNumber, conv.customer_id, conv.session_id,
        JSON.stringify(cart),
        cart.reduce((s,i)=>s+i.line_total,0),
        c.shipping_final, c.total,
        c.name, conv.phone, c.address, c.governorate, c.payment_method,
      ]);

      // احتجز المخزون
      await this.reserveStock(cart, order.id);

      // ═══ الدفع أونلاين؟ ═══
      if (c.payment_method !== 'cod') {
        const link = await this.paymentLink(order);
        await this.reply(conv, `
✅ *تم تسجيل الطلب!*

📦 رقم الطلب: *${orderNumber}*
💰 الإجمالي: *${c.total}ج*

للدفع (صالح 30 دقيقة) 👇
${link}

بعد الدفع هتوصلك رسالة تأكيد فوراً 💚
        `);
        await this.schedulePaymentReminder(order.id, 15 * 60_000);
      } else {
        await this.reply(conv, spin(`
✅ *{تم|اتسجّل} الطلب {بنجاح|}!*

📦 رقم الطلب: *${orderNumber}*
💰 الإجمالي: *${c.total}ج* (كاش عند الاستلام)
🚚 التوصيل: خلال 2-4 أيام

هنكلمك خلال ساعة للتأكيد ☎️

{شكراً|متشكرين} {ليك|جداً} 💚
        `));
      }

      await this.goto(conv, STATES.DONE);

      // ═══ التنبيهات والتكامل ═══
      await Promise.allSettled([
        // تنبيه فريق المبيعات
        this.alerter.send(`
🛒 *أوردر جديد #${orderNumber}*
👤 ${c.name} — ${conv.phone}
📍 ${c.governorate}
💰 ${c.total}ج (${c.payment_method})
📦 ${cart.map(i => `${i.product_name}×${i.qty}`).join(' · ')}
📱 من: ${conv.session_id}
        `, { level: 'ok' }),

        // Chatwoot labels
        this.chatwoot?.addLabels(conv.chatwoot_conv_id, [
          'order:new', `value:${c.total > 1000 ? 'high' : 'normal'}`,
        ]),

        // نظام الأوردرات الخارجي
        this.pushToERP(order),

        // تحديث بيانات العميل
        this.db.query(`
          UPDATE customers
          SET last_order_at = NOW(), name = COALESCE(name, $1)
          WHERE id = $2
        `, [c.name, conv.customer_id]),

        // إحصاء الحملة
        this.db.query(`
          UPDATE campaigns SET orders = orders + 1, revenue = revenue + $1
          WHERE id = (
            SELECT campaign_id FROM message_log
            WHERE phone = $2 AND direction='out'
            ORDER BY created_at DESC LIMIT 1
          )
        `, [c.total, conv.phone]),
      ]);

    } finally {
      await releaseLock(lock);
    }
  }

  // ═══════════════ أدوات ═══════════════

  async reply(conv, text) {
    const clean = text.replace(/^\s+|\s+$/g, '').replace(/\n{3,}/g, '\n\n');
    await this.sender.send({
      sessionId: conv.session_id,
      phone: conv.phone,
      content: { type: 'text', text: clean },
      withTyping: true,
      priority: 'high',       // ← ردود البوت أولوية عالية
      bypassRateLimit: true,  // ← الردود مش outbound campaign
    });
    await this.db.query(`
      INSERT INTO message_log (session_id, phone, direction, content, status)
      VALUES ($1,$2,'out',$3,'sent')
    `, [conv.session_id, conv.phone, clean]);
  }

  async fail(conv, hint) {
    const n = (conv.context.failedParses ?? 0) + 1;
    await this.setContext(conv, { failedParses: n });

    if (n >= 3) {
      await this.reply(conv, 'خليني أوصلك بموظف يساعدك أحسن 👍');
      await doHandoff(conv, 'bot_confused');
      return;
    }
    await this.reply(conv, `${hint}\n\n_(أو ابعت *0* لموظف)_`);
  }

  async goto(conv, state) {
    conv.state = state;
    await this.db.query(`
      UPDATE conversations
      SET state=$1, context = context - 'failedParses',
          expires_at = NOW() + INTERVAL '30 minutes',
          updated_at = NOW()
      WHERE id=$2
    `, [state, conv.id]);
    await this.render(conv);
  }

  async setContext(conv, patch) {
    conv.context = { ...conv.context, ...patch };
    await this.db.query(
      `UPDATE conversations SET context = context || $1 WHERE id=$2`,
      [JSON.stringify(patch), conv.id]
    );
  }

  async setCart(conv, cart) {
    conv.cart = cart;
    await this.db.query(
      `UPDATE conversations SET cart=$1 WHERE id=$2`,
      [JSON.stringify(cart), conv.id]
    );
  }
}
```

### دوال مساعدة للعربي

```javascript
/** توحيد الحروف العربية للمقارنة */
function normalizeArabic(s) {
  if (!s) return '';
  return s.toString().trim().toLowerCase()
    .replace(/[\u0623\u0625\u0622\u0671]/g, 'ا')  // أإآٱ → ا
    .replace(/\u0649/g, 'ي')                       // ى → ي
    .replace(/\u0629/g, 'ه')                       // ة → ه
    .replace(/\u0624/g, 'و')                       // ؤ → و
    .replace(/\u0626/g, 'ي')                       // ئ → ي
    .replace(/[\u064B-\u0652\u0640]/g, '')         // تشكيل وتطويل
    .replace(/\s+/g, ' ');
}

/** أرقام عربية/فارسية → إنجليزية */
function normalizeDigits(s) {
  if (!s) return '';
  const ar = '٠١٢٣٤٥٦٧٨٩';
  const fa = '۰۱۲۳۴۵۶۷۸۹';
  return s.toString().replace(/[٠-٩۰-۹]/g, ch => {
    const i = ar.indexOf(ch);
    return i > -1 ? i : fa.indexOf(ch);
  });
}

/** رصد النية الإيجابية */
const POSITIVE = [
  'ايوه','اه','ايوة','نعم','تمام','ماشي','اوك','حاضر','اكيد',
  'موافق','عايز','عاوز','محتاج','هاخد','هاخده','ابعت','ابعتلي',
  'yes','ok','okay','sure','yalla','yes please',
];
function isPositive(text) {
  const t = normalizeArabic(text);
  return POSITIVE.some(k => t === k || t.startsWith(k + ' ') || t.endsWith(' ' + k));
}

const NEGATIVE = [
  'لا','مش','لأ','مش عايز','مش محتاج','بعدين','مش دلوقتي',
  'شكرا','متشكر','no','not now','later','thanks',
];
function isNegative(text) {
  const t = normalizeArabic(text);
  return NEGATIVE.some(k => t === k || t.startsWith(k));
}
```

---

## 2. مسار صفحة الهبوط (Landing Page)

### بناء الرابط المخصص

```javascript
import jwt from 'jsonwebtoken';

function buildOfferLink(customer, campaign) {
  // 🔐 توكن موقّع — العميل ميقدرش يعدّل البيانات
  const token = jwt.sign({
    cid: customer.id,
    cmp: campaign.id,
    sid: customer.assigned_session,
    ph:  customer.phone,      // للتعبئة التلقائية
    nm:  customer.name,
    seg: customer.segment,
    dsc: campaign.discount,   // نسبة الخصم المخصصة
  }, process.env.JWT_SECRET, { expiresIn: '14d' });

  const url = new URL('https://shop.yourdomain.com/offer');
  url.searchParams.set('t', token);

  // UTM للتحليلات
  url.searchParams.set('utm_source',   'whatsapp');
  url.searchParams.set('utm_medium',   'direct_message');
  url.searchParams.set('utm_campaign', campaign.slug);
  url.searchParams.set('utm_content',  customer.segment);

  return url.toString();
}
```

### صفحة الهبوط (Next.js)

```typescript
// app/offer/page.tsx
import jwt from 'jsonwebtoken';
import { redirect } from 'next/navigation';

export default async function OfferPage({
  searchParams,
}: { searchParams: { t?: string } }) {

  if (!searchParams.t) redirect('/');

  let payload: any;
  try {
    payload = jwt.verify(searchParams.t, process.env.JWT_SECRET!);
  } catch {
    // توكن منتهي → عرض عام
    return <GenericOffer />;
  }

  // ✅ سجّل الزيارة (attribution)
  await trackVisit({
    customerId: payload.cid,
    campaignId: payload.cmp,
    at: new Date(),
  });

  const customer = await getCustomer(payload.cid);
  const products = await getRecommended(payload.cid);

  return (
    <main dir="rtl" className="min-h-dvh bg-neutral-50">
      {/* ترحيب شخصي */}
      <section className="bg-gradient-to-b from-emerald-600 to-emerald-700 text-white px-5 py-8">
        <p className="text-emerald-100 text-sm">أهلاً بيك</p>
        <h1 className="text-2xl font-bold mt-1">{customer.name} 👋</h1>
        <div className="mt-4 inline-flex items-center gap-2 bg-white/15 rounded-full px-4 py-2">
          <span className="text-3xl font-black">{payload.dsc}%</span>
          <span className="text-sm">خصم خاص ليك</span>
        </div>
      </section>

      {/* المنتجات */}
      <section className="px-5 py-6">
        <h2 className="font-bold text-lg mb-4">مختارة مخصوص ليك</h2>
        <div className="grid gap-4">
          {products.map(p => (
            <ProductCard
              key={p.id}
              product={p}
              discount={payload.dsc}
            />
          ))}
        </div>
      </section>

      {/* 🔑 Checkout مملوء مسبقاً */}
      <CheckoutForm
        prefill={{
          name:  customer.name,
          phone: customer.phone,
          city:  customer.city,
          address: customer.last_address,
        }}
        customerId={payload.cid}
        campaignId={payload.cmp}
        sessionId={payload.sid}
        discount={payload.dsc}
        // 🎯 حقول مقفولة (تقليل الاحتكاك)
        lockedFields={['phone']}
      />

      {/* زر رجوع للواتساب */}
      <a
        href={`https://wa.me/${payload.sid_phone}?text=${
          encodeURIComponent('عايز أسأل عن العرض')
        }`}
        className="fixed bottom-5 left-5 bg-emerald-500 text-white
                   rounded-full p-4 shadow-xl"
      >
        💬 اسأل على واتساب
      </a>
    </main>
  );
}
```

### تقليل الاحتكاك (Friction Reduction)

```
الهدف: أقل عدد خطوات لإتمام الشراء

┌────────────────────────────────────────┐
│ ❌ Checkout عادي — 12 حقل، 3 خطوات    │
│    معدل الإكمال: ~20%                  │
├────────────────────────────────────────┤
│ ✅ Checkout مملوء — 2 حقل، خطوة واحدة │
│    معدل الإكمال: ~55%                  │
└────────────────────────────────────────┘

اللي تعمله:
✅ الاسم والتليفون: مملوءين ومقفولين (من التوكن)
✅ المحافظة: مختارة (من آخر أوردر)
✅ العنوان: مملوء وقابل للتعديل
✅ الخصم: مطبّق تلقائياً (مفيش كود يكتبه)
✅ الدفع: COD مختار افتراضياً (الأشهر في مصر)
✅ صفحة واحدة، بدون تسجيل حساب
✅ زر واحد كبير: "تأكيد الطلب"
```

### Webhook بعد الطلب

```javascript
// POST /api/orders/create
export async function POST(req) {
  const body = await req.json();

  // 1. تحقق من التوكن
  let payload;
  try {
    payload = jwt.verify(body.token, process.env.JWT_SECRET);
  } catch {
    return Response.json({ error: 'رابط منتهي' }, { status: 400 });
  }

  // 2. 🔒 التليفون من التوكن، مش من الفورم (منع التلاعب)
  const phone = payload.ph;

  // 3. أنشئ الأوردر
  const order = await createOrder({
    ...body,
    phone,
    customerId: payload.cid,
    campaignId: payload.cmp,
    channel: 'landing_page',
    discount: payload.dsc,
  });

  // 4. ✨ تأكيد فوري على الواتساب — من نفس الرقم اللي كلّمه!
  await statusQueue.add('order_confirmation', {
    sessionId: payload.sid,       // 🔑 نفس الجلسة = تجربة متصلة
    phone,
    orderId: order.id,
    template: 'order_confirmed',
  }, {
    priority: 1,
    delay: 3000 + Math.random() * 7000,  // 3-10 ثواني (طبيعي)
  });

  // 5. إحصاء الحملة
  await db.query(`
    UPDATE campaigns
    SET orders = orders + 1, revenue = revenue + $1
    WHERE id = $2
  `, [order.total, payload.cmp]);

  return Response.json({
    ok: true,
    orderNumber: order.order_number,
    redirect: `/thank-you?o=${order.order_number}`,
  });
}
```

---

## 3. قوالب رسائل حالة الأوردر

```javascript
const STATUS_TEMPLATES = {

  order_confirmed: (o) => spin(`
✅ *{تم تأكيد|اتأكد} طلبك!*

📦 رقم الطلب: *${o.order_number}*
💰 الإجمالي: ${o.total}ج
🚚 {التوصيل|الوصول}: ${o.eta}

${o.items.map(i => `• ${i.product_name} ×${i.qty}`).join('\n')}

{هنبعتلك|هنكلمك} {أول ما|لما} يخرج للشحن 📦
  `),

  order_preparing: (o) => spin(`
📦 {بنجهّز|بنحضّر} طلبك *${o.order_number}* {دلوقتي|الآن}

{هيخرج|بيخرج} للشحن خلال ${o.prep_hours} ساعة ⏱️
  `),

  order_shipped: (o) => spin(`
🚚 *طلبك في الطريق!*

📦 ${o.order_number}
🔢 رقم التتبع: *${o.tracking_number}*
🏢 شركة الشحن: ${o.courier}
📅 التوصيل المتوقع: ${o.eta}

${o.tracking_url ? `تتبع: ${o.tracking_url}` : ''}

☎️ المندوب هيكلمك قبل الوصول
  `),

  out_for_delivery: (o) => spin(`
🛵 *المندوب في الطريق ليك!*

طلب ${o.order_number}
${o.payment_method === 'cod' ? `💵 المبلغ المطلوب: *${o.total}ج* كاش` : '✅ مدفوع'}

{جهّز|خد بالك من} ${o.payment_method === 'cod' ? 'المبلغ' : 'الاستلام'} 👍
  `),

  order_delivered: (o) => spin(`
✅ *{تم التوصيل|وصل}!*

{شكراً|متشكرين} {ليك|جداً} على {ثقتك|طلبك} 💚

{رأيك|تقييمك} {مهم|يفرق} {لينا|جداً}:
⭐ *1* ممتاز
🙂 *2* كويس
😐 *3* عادي
😞 *4* فيه مشكلة

_ردك بيساعدنا نتحسن_
  `),

  order_cancelled: (o) => spin(`
❌ {تم إلغاء|اتلغى} الطلب *${o.order_number}*

${o.cancel_reason ? `السبب: ${o.cancel_reason}` : ''}
${o.refund ? `💰 هيتم رد ${o.total}ج خلال 3-5 أيام عمل` : ''}

{لو عندك|في} {استفسار|سؤال}؟ ابعت *0* وموظف هيرد عليك
  `),

  payment_reminder: (o) => spin(`
⏰ {فاكرك|تذكير} — طلب *${o.order_number}*

{لسه|الطلب} محجوز {ليك|} لمدة ${o.mins_left} دقيقة

{للدفع|أكمل الدفع} 👇
${o.payment_link}

_بعد كده الطلب هيتلغى تلقائياً_
  `),
};
```

> ⚠️ **ملاحظة مهمة:** رسائل حالة الأوردر (Transactional) العميل بيستناها. تقدر تبعتها **حتى من الرقم الرسمي** بأمان تام، لأن:
> - العميل هو اللي بدأ العلاقة (اشترى)
> - الرسالة متوقعة ومفيدة
> - احتمال البلاغ ≈ 0
>
> ده بالعكس تماماً عن الرسائل الترويجية.

---

## 4. استعادة السلة المتروكة (Cart Recovery)

```javascript
/**
 * ⚠️ تكتيك خطر لو استخدمته غلط
 * القاعدة: مرة واحدة بس، بعد وقت مناسب، برسالة مساعدة (مش بيعية)
 */
async function abandonedCartRecovery() {
  const abandoned = await db.many(`
    SELECT c.*, cu.name, cu.phone, cu.assigned_session
    FROM conversations c
    JOIN customers cu ON cu.id = c.customer_id
    WHERE c.state IN ('cart_review','ask_name','ask_address',
                      'ask_payment','confirm_order')
      AND c.updated_at BETWEEN NOW() - INTERVAL '6 hours'
                           AND NOW() - INTERVAL '90 minutes'
      AND jsonb_array_length(c.cart) > 0
      AND NOT EXISTS (
        SELECT 1 FROM message_log m
        WHERE m.phone = c.phone
          AND m.content LIKE '%سلة%'
          AND m.created_at > NOW() - INTERVAL '7 days'
      )
      AND NOT EXISTS (
        SELECT 1 FROM suppression_list s WHERE s.phone = c.phone
      )
  `).catch(() => []);

  for (const conv of abandoned) {
    const cart = conv.cart;
    const total = cart.reduce((s, i) => s + i.line_total, 0);

    // ✅ رسالة مساعدة، مش بيعية
    await replyQueue.add('cart_recovery', {
      sessionId: conv.assigned_session,
      phone: conv.phone,
      text: spin(`
{أهلاً|ازيك} ${conv.name} 👋

{شكلك|يبدو إنك} {اتشغلت|مشغول} {وسط|في نص} الطلب.

🛒 السلة {لسه|محفوظة}:
${cart.map(i => `• ${i.product_name} ×${i.qty}`).join('\n')}
الإجمالي: ${total}ج

{تحب|حابب} {نكمّل|أكمّل معاك}؟ ابعت *1* 👍
{أو|ولو} {في|عندك} {حاجة|سؤال} {مش واضحة|} {قولي|اسألني}.

_لو مش مهتم، متردش وهسيبك 🙏_
      `),
    }, { delay: Math.random() * 1800_000 });   // توزيع على 30 دقيقة
  }
}
```

### 🔴 قواعد الاستعادة

```
✅ اعمل:
   • مرة واحدة فقط (متبعتش مرتين!)
   • بعد 1.5 - 6 ساعات (مش فوراً — مزعج)
   • في نافذة الوقت المناسبة (مش 2 صباحاً)
   • من نفس الرقم اللي كان بيتكلم معاه
   • رسالة "مساعدة" مش "بيع"
   • بدون خصم في المحاولة الأولى

❌ متعملش:
   • متبعتش تاني لو مردش
   • متبعتش "آخر فرصة!!" أو ضغط
   • متبعتش لو العميل كان قال "مش عايز"
   • متعملش 3 محاولات — دي أسرع طريقة للبلاغ
```

---

## 5. قياس النتائج (Attribution)

```sql
CREATE VIEW v_funnel_by_segment AS
WITH sent AS (
  SELECT
    m.campaign_id, cu.segment,
    COUNT(DISTINCT m.phone) AS sent
  FROM message_log m
  JOIN customers cu ON cu.id = m.customer_id
  WHERE m.direction = 'out'
  GROUP BY 1, 2
),
delivered AS (
  SELECT m.campaign_id, cu.segment,
         COUNT(DISTINCT m.phone) AS delivered
  FROM message_log m
  JOIN customers cu ON cu.id = m.customer_id
  WHERE m.direction='out' AND m.status IN ('delivered','read')
  GROUP BY 1, 2
),
replied AS (
  SELECT m.campaign_id, cu.segment,
         COUNT(DISTINCT m.phone) AS replied
  FROM message_log m
  JOIN customers cu ON cu.id = m.customer_id
  WHERE m.direction = 'in'
  GROUP BY 1, 2
),
ordered AS (
  SELECT o.campaign_id, cu.segment,
         COUNT(*) AS orders,
         SUM(o.total) AS revenue
  FROM orders o
  JOIN customers cu ON cu.id = o.customer_id
  GROUP BY 1, 2
),
opted AS (
  SELECT m.campaign_id, cu.segment, COUNT(*) AS optouts
  FROM suppression_list s
  JOIN customers cu ON cu.phone = s.phone
  JOIN message_log m ON m.phone = s.phone AND m.direction='out'
  WHERE s.reason = 'user_opt_out'
  GROUP BY 1, 2
)
SELECT
  s.campaign_id, s.segment,
  s.sent,
  d.delivered,
  r.replied,
  o.orders,
  o.revenue,
  p.optouts,
  ROUND(100.0*d.delivered/NULLIF(s.sent,0),  1) AS delivery_pct,
  ROUND(100.0*r.replied/NULLIF(s.sent,0),    1) AS reply_pct,
  ROUND(100.0*o.orders/NULLIF(s.sent,0),     2) AS conv_pct,
  ROUND(100.0*p.optouts/NULLIF(s.sent,0),    2) AS optout_pct,
  ROUND(o.revenue/NULLIF(o.orders,0),        0) AS aov,
  ROUND(o.revenue/NULLIF(s.sent,0),          2) AS revenue_per_msg
FROM sent s
LEFT JOIN delivered d USING (campaign_id, segment)
LEFT JOIN replied   r USING (campaign_id, segment)
LEFT JOIN ordered   o USING (campaign_id, segment)
LEFT JOIN opted     p USING (campaign_id, segment)
ORDER BY o.revenue DESC NULLS LAST;
```

### معايير مرجعية (Benchmarks) — واتساب لعملاء حاليين

| المقياس | ضعيف | مقبول | كويس | ممتاز |
|---|---|---|---|---|
| Delivery Rate | <80% | 80-90% | 90-96% | >96% |
| Read Rate | <50% | 50-70% | 70-85% | >85% |
| **Reply Rate** | <5% | 5-12% | 12-25% | >25% |
| Conversion | <1% | 1-3% | 3-7% | >7% |
| **Opt-out Rate** | >4% | 2-4% | 0.5-2% | <0.5% |

> ⚠️ الـ **Opt-out Rate** هو مؤشر السلامة الأول. لو عدّى 3%، وقّف الحملة **فوراً** — مش بس بسبب الحظر، ده معناه إنك بتحرق قاعدة عملائك.

---

**التالي:** [`06-IMPLEMENTATION.md`](./06-IMPLEMENTATION.md) — كود عملي وخطة تنفيذ

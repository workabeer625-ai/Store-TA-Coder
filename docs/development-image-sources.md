# Development image sources

The local files under `backend/Marina.Api/wwwroot/uploads/demo-*.jpg` are development-only copies downloaded from Unsplash. They are not hotlinks. Source URLs:

- Dress: `photo-1595777457583-95e059d581b8`, `photo-1566174053879-31528523f8ae`
- Shoes: `photo-1542291026-7eec264c27ff`, `photo-1460353581641-37baddab0fa2`
- Kids clothing: `photo-1519238263530-99bdd11df2ea`, `photo-1622290291468-a28f7a7dc6a8`
- Accessories: `photo-1553062407-98eeb64c6a62`, `photo-1584917865442-de89df76afd3`

Downloads used HTTPS, a 25-second timeout, an 8 MB transfer cap, and JPEG signature verification. Runtime uploads additionally decode the image and enforce width, height, and pixel-count limits.

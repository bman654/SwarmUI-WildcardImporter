import torch, comfy

class WCCompositeMask:
    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "mask_a": ("MASK",),
                "mask_b": ("MASK",),
                "op": (["max", "min"],),
            }
        }

    CATEGORY = "WC/mask"
    RETURN_TYPES = ("MASK",)
    FUNCTION = "combine"
    DESCRIPTION = "Combines two masks using the specified operator."

    def combine(self, mask_a, mask_b, op):
        output = mask_b.reshape((-1, mask_b.shape[-2], mask_b.shape[-1])).clone()
        source = mask_a.reshape((-1, mask_a.shape[-2], mask_a.shape[-1]))
    
        left, top = (0, 0,)
        right, bottom = (min(left + source.shape[-1], mask_b.shape[-1]), min(top + source.shape[-2], mask_b.shape[-2]))
        visible_width, visible_height = (right - left, bottom - top,)
    
        source_portion = source[:, :visible_height, :visible_width]
        destination_portion = output[:, top:bottom, left:right]
    
        if op == "max":
            output[:, top:bottom, left:right] = torch.fmax(destination_portion, source_portion)
        elif op == "min":
            output[:, top:bottom, left:right] = torch.fmin(destination_portion, source_portion)
    
        return (output,)


NODE_CLASS_MAPPINGS = {
    "WCCompositeMask": WCCompositeMask,
}
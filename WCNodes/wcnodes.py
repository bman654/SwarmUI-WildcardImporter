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

    CATEGORY = "WC/masks"
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

# Adapted from SwarmMaskBounds
class WCMaskBounds:
    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "mask": ("MASK",),
                "grow": ("INT", {"default": 0, "min": 0, "max": 1024, "tooltip": "Number of pixels to grow the mask by."}),
            },
            "optional": {
                "aspect_x": ("INT", {"default": 0, "min": 0, "max": 4096, "tooltip": "An X width value, used to indicate a target aspect ratio. 0 to allow any aspect."}),
                "aspect_y": ("INT", {"default": 0, "min": 0, "max": 4096, "tooltip": "A Y height value, used to indicate a target aspect ratio. 0 to allow any aspect."}),
                "dynamic": ("BOOLEAN", {"default": False, "tooltip": "If true, the aspect_x/y are only used to indicate overall minimum target pixel count the actual resolution will be chosen intelligently based upon mask size."}),
            }
        }

    CATEGORY = "WC/masks"
    RETURN_TYPES = ("INT", "INT", "INT", "INT")
    RETURN_NAMES = ("x", "y", "width", "height")
    FUNCTION = "get_bounds"
    DESCRIPTION = "Returns the bounding box of the mask (as pixel coordinates x,y,width,height), optionally grown by the number of pixels specified in 'grow' and then optionally adjusted for aspect ratio."

    def get_bounds(self, mask, grow, aspect_x=0, aspect_y=0, dynamic=False):
        if len(mask.shape) == 3:
            mask = mask[0]
        sum_x = (torch.sum(mask, dim=0) != 0).to(dtype=torch.int)
        sum_y = (torch.sum(mask, dim=1) != 0).to(dtype=torch.int)
        def getval(arr, direction):
            val = torch.argmax(arr).item()
            val += grow * direction
            val = max(0, min(val, arr.shape[0] - 1))
            return val
        x_start = getval(sum_x, -1)
        x_end = mask.shape[1] - getval(sum_x.flip(0), -1)
        y_start = getval(sum_y, -1)
        y_end = mask.shape[0] - getval(sum_y.flip(0), -1)
        if aspect_x > 0 and aspect_y > 0:
            input_aspect = aspect_x / aspect_y
            width = x_end - x_start
            height = y_end - y_start
            actual_aspect = width / height
            if dynamic:
                allowed_aspect_ratios = [1, 4/3, 3/2, 8/5, 16/9, 21/9, 3/4, 2/3, 5/8, 9/16, 9/21, input_aspect]
                input_aspect = min(allowed_aspect_ratios, key=lambda x: abs(x - actual_aspect))
            if actual_aspect > input_aspect:
                desired_height = width / input_aspect
                y_start = max(0, y_start - (desired_height - height) / 2)
                y_end = min(mask.shape[0], y_start + desired_height)
            else:
                desired_width = height * input_aspect
                x_start = max(0, x_start - (desired_width - width) / 2)
                x_end = min(mask.shape[1], x_start + desired_width)
        return (int(x_start), int(y_start), int(x_end - x_start), int(y_end - y_start))


class WCSkipIfMaskEmpty:
    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "mask": ("MASK",),
                "image_if_empty": ("IMAGE",{"lazy": True}),
                "image_if_not_empty": ("IMAGE",{"lazy": True}),
            }
        }

    CATEGORY = "WC/masks"
    RETURN_TYPES = ("IMAGE",)
    FUNCTION = "route"
    DESCRIPTION = "If the mask is empty, returns the 'image_if_empty' image. Otherwise, returns the 'image_if_not_empty' image.  Only evaluates the input image that is going to be returned."

    def check_lazy_status(self, mask, image_if_empty, image_if_not_empty):
        mask_max = mask.max()
        if mask_max == 0 and image_if_empty is None:
            return ["image_if_empty"]
        elif mask_max > 0 and image_if_not_empty is None:
            return ["image_if_not_empty"]
        return []
    
    def route(self, mask, image_if_empty, image_if_not_empty):
        mask_max = mask.max()
        if mask_max == 0:
            return (image_if_empty,)
        else:
            return (image_if_not_empty,)

NODE_CLASS_MAPPINGS = {
    "WCCompositeMask": WCCompositeMask,
    "WCMaskBounds": WCMaskBounds,
    "WCSkipIfMaskEmpty": WCSkipIfMaskEmpty,
}